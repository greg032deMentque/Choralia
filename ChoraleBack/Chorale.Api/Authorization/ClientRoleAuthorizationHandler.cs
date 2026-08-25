using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Services.ClientServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace ChoraleBackEnd.Api.Authorization;

public sealed class ClientRoleRequirement : IAuthorizationRequirement
{
    public UserRoleEnum[] AllowedRoles { get; }

    public ClientRoleRequirement(params UserRoleEnum[] allowedRoles)
    {
        AllowedRoles = allowedRoles;
    }
}

/// <summary>
/// Valide un role scope au <b>client</b> (`10-D23`).
/// </summary>
/// <remarks>
/// Le scope client est resolu dans cet ordre, jamais en melangeant les sources pour une
/// meme requete : (1) la route (`clientId`) ; (2) la ressource chorale VISEE, quand la
/// requete porte un identifiant de chorale (`id`, en route ou en query string) — c'est le
/// cas d'Update et de Delete, ou le client s'y deduit de ce qui est deja stocke, jamais de
/// ce que l'appelant declare ; (3) en dernier repli, le corps de la requete, pour les
/// actions qui creent une ressource qui n'existe pas encore (Create) et n'ont donc rien a
/// deduire.
///
/// L'ordre (2) avant (3) est le point de securite : lire le `ClientId` du corps pour une
/// action qui porte sur une chorale EXISTANTE permettrait a un ResponsableClient de update
/// la chorale d'un autre client en declarant le sien dans le corps, alors que la policy et
/// le service doivent toujours verifier la MEME valeur — celle de la ressource, pas celle
/// annoncee par l'appelant.
/// </remarks>
public sealed class ClientRoleAuthorizationHandler : AuthorizationHandler<ClientRoleRequirement>
{
    public const string ClientIdRouteName = "clientId";
    public const string ClientIdBodyFieldName = "ClientId";
    public const string ChoirResourceIdParamName = "id";

    /// <summary>
    /// Repli supplementaire pour les routes imbriquees sous une chorale
    /// (<c>api/choirs/{choirId}/...</c>, ex. <c>ChoirMastersController</c>) — le nom de
    /// segment de route etabli ailleurs dans <c>ChoirController</c>
    /// (<c>{choirId:guid}/AddMember</c>) plutot que <c>id</c>, utilise lui par Update/Delete.
    /// Les deux noms cohabitent sans jamais se melanger : chaque route n'en porte qu'un.
    /// </summary>
    public const string ChoirIdRouteName = "choirId";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IClientRoleResolverService _clientRoleResolverService;

    public ClientRoleAuthorizationHandler(
        IHttpContextAccessor httpContextAccessor,
        IClientRoleResolverService clientRoleResolverService)
    {
        _httpContextAccessor = httpContextAccessor;
        _clientRoleResolverService = clientRoleResolverService;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ClientRoleRequirement requirement)
    {
        // L'administration generale garde l'acces : c'est elle qui cree les clients et fixe
        // leurs plafonds. Contrairement au handler d'espace, il n'y a pas ici de contenu de
        // chorale a proteger — la lecture de contenu passe par les policies scopees espace.
        if (context.User.Claims.Any(c =>
                c.Type == ClaimTypes.Role && c.Value == UserRoleEnum.Admin.ToString()))
        {
            context.Succeed(requirement);
            return;
        }

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return;

        var clientId = ExtractClientId()
            ?? await ExtractClientIdFromChoirResourceAsync()
            ?? await ExtractClientIdFromBodyAsync();
        if (clientId is null)
            return;

        var roles = await _clientRoleResolverService.ResolveRolesAsync(userId, clientId.Value);

        if (requirement.AllowedRoles.Any(roles.Contains))
            context.Succeed(requirement);
    }

    private Guid? ExtractClientId()
    {
        var routeValue = _httpContextAccessor.HttpContext?.Request
            .RouteValues[ClientIdRouteName]?.ToString();

        return Guid.TryParse(routeValue, out var clientId) ? clientId : null;
    }

    /// <summary>
    /// Deduit le client d'une chorale EXISTANTE, visee par <c>id</c> (route ou query
    /// string — cas d'Update et Delete, ou <c>id</c> n'est pas dans le gabarit de route et
    /// n'atteint donc jamais <c>RouteValues</c>). Retourne <c>null</c> si la requete ne porte
    /// aucun <c>id</c> exploitable (Create, qui ne vise aucune ressource existante) ou si la
    /// chorale est introuvable — dans ce dernier cas le service, qui charge la meme chorale,
    /// levera l'erreur appropriee.
    /// </summary>
    private async Task<Guid?> ExtractClientIdFromChoirResourceAsync()
    {
        var resourceId = ExtractResourceId();
        if (resourceId is null)
            return null;

        return await _clientRoleResolverService.ResolveChoirClientIdAsync(resourceId.Value);
    }

    private Guid? ExtractResourceId()
    {
        var routeValue = _httpContextAccessor.HttpContext?.Request
            .RouteValues[ChoirResourceIdParamName]?.ToString();
        if (Guid.TryParse(routeValue, out var routeId))
            return routeId;

        var choirIdRouteValue = _httpContextAccessor.HttpContext?.Request
            .RouteValues[ChoirIdRouteName]?.ToString();
        if (Guid.TryParse(choirIdRouteValue, out var choirRouteId))
            return choirRouteId;

        var queryValue = _httpContextAccessor.HttpContext?.Request
            .Query[ChoirResourceIdParamName].ToString();
        return Guid.TryParse(queryValue, out var queryId) ? queryId : null;
    }

    /// <summary>
    /// Repli sur le corps de la requete quand la route ne porte pas de <c>clientId</c> —
    /// c'est le cas de la creation d'une chorale (`POST /api/choirs/Create`), qui le porte
    /// dans le corps. Essentiel que la policy lise ici la MEME valeur que le service : sinon
    /// la policy autoriserait sur un client et le service ecrirait dans un autre.
    /// </summary>
    private async Task<Guid?> ExtractClientIdFromBodyAsync()
    {
        var request = _httpContextAccessor.HttpContext?.Request;
        if (request is null || !request.ContentLength.HasValue || request.ContentLength == 0)
            return null;

        request.EnableBuffering();
        request.Body.Position = 0;

        string body;
        using (var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true))
            body = await reader.ReadToEndAsync();

        request.Body.Position = 0;

        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!string.Equals(property.Name, ClientIdBodyFieldName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (property.Value.ValueKind == JsonValueKind.String
                    && Guid.TryParse(property.Value.GetString(), out var clientId))
                    return clientId;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
