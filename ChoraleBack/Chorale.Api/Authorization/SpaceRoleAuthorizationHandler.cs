using System.Security.Claims;
using ChoraleBackEnd.Services.AuthServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace ChoraleBackEnd.Api.Authorization;

public sealed class SpaceRoleAuthorizationHandler : AuthorizationHandler<SpaceRoleRequirement>
{
    public const string SpaceIdHeaderName = "X-Space-Id";
    public const string ChoirIdHeaderName = "X-Chorale-Id";
    public const string SpaceIdRouteName = "spaceId";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ISpaceRoleResolverService _spaceRoleResolverService;

    public SpaceRoleAuthorizationHandler(
        IHttpContextAccessor httpContextAccessor, ISpaceRoleResolverService spaceRoleResolverService)
    {
        _httpContextAccessor = httpContextAccessor;
        _spaceRoleResolverService = spaceRoleResolverService;
    }

    /// <summary>
    /// Aucun bypass Admin ici. Cette policy scope l'ecriture de contenu d'un espace
    /// (choir ou evenement) — `Manager`, `Organizer`, `SectionLeader`. Decision
    /// produit (`10-D23`) : l'administration generale a acces en LECTURE a tout, mais aucune
    /// ecriture de contenu, meme en mode support. La lecture, elle, passe par
    /// <see cref="ISpaceAccessAuditService"/>, qui journalise
    /// systematiquement l'acces.
    /// </summary>
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, SpaceRoleRequirement requirement)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return;

        var spaceId = ExtractSpaceId();
        if (spaceId is null)
            return;

        var rolesBySpace = await _spaceRoleResolverService.ResolveRolesAsync(userId, [spaceId.Value]);
        if (!rolesBySpace.TryGetValue(spaceId.Value, out var effectiveRoles))
            return;

        if (requirement.AllowedRoles.Any(effectiveRoles.Contains))
            context.Succeed(requirement);
    }

    private Guid? ExtractSpaceId()
    {
        var headerValue = _httpContextAccessor.HttpContext?.Request.Headers[SpaceIdHeaderName]
            .ToString();
        if (Guid.TryParse(headerValue, out var spaceId))
            return spaceId;

        var fallbackValue = _httpContextAccessor.HttpContext?.Request.Headers[ChoirIdHeaderName]
            .ToString();
        if (Guid.TryParse(fallbackValue, out var choirId))
            return choirId;

        // Repli route (`spaceId`) : les endpoints imbriques `api/spaces/{spaceId}/...`
        // (lot 6, code de rattachement et demandes d'adhesion) portent l'identifiant dans
        // l'URL plutot que dans un en-tete — les deux sources cohabitent sans se melanger,
        // chaque appelant n'en fournit qu'une seule.
        var routeValue = _httpContextAccessor.HttpContext?.Request.RouteValues[SpaceIdRouteName]?.ToString();
        return Guid.TryParse(routeValue, out var routeSpaceId) ? routeSpaceId : null;
    }
}
