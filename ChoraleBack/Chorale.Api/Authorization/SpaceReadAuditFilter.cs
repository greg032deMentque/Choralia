using ChoraleBackEnd.Services.AuthServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ChoraleBackEnd.Api.Authorization;

/// <summary>
/// Trace tout acces au contenu d'un espace traverse par l'administration generale (`02 §66`).
/// </summary>
/// <remarks>
/// Filtre d'action et non appel de service : les endpoints concernes delegent a des services
/// PARTAGES avec le parcours non-admin (<c>IChoirMembersService</c>, <c>ISongService</c>,
/// <c>IEventService</c>). Y placer l'audit tracerait aussi la lecture d'un simple choriste,
/// et l'appeler depuis le controleur y remonterait de la logique d'acces. Le filtre garde le
/// controleur thin et couvre chaque route admin portant un identifiant d'espace, sans que
/// l'auteur d'un futur endpoint ait a y penser — il suffit de poser l'attribut.
///
/// <see cref="ISpaceAccessAuditService.EnsureReadAsync"/> porte AUSSI le refus : un appelant
/// sans role effectif sur l'espace recoit 404, jamais 403 (reveler l'existence est deja une fuite).
/// </remarks>
public sealed class SpaceReadAuditFilter : IAsyncActionFilter
{
    private static readonly string[] SpaceRouteKeys = ["choirId", "spaceId", "eventId"];

    private readonly ISpaceAccessAuditService _spaceAccessAuditService;

    public SpaceReadAuditFilter(ISpaceAccessAuditService spaceAccessAuditService)
        => _spaceAccessAuditService = spaceAccessAuditService;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var key in SpaceRouteKeys)
        {
            if (context.RouteData.Values.TryGetValue(key, out var raw)
                && Guid.TryParse(raw?.ToString(), out var spaceId))
            {
                await _spaceAccessAuditService.EnsureReadAsync(spaceId, context.HttpContext.RequestAborted);
                break;
            }
        }

        await next();
    }
}

/// <summary>Pose <see cref="SpaceReadAuditFilter"/> sur une action ou un controleur.</summary>
public sealed class SpaceReadAuditAttribute() : TypeFilterAttribute(typeof(SpaceReadAuditFilter));
