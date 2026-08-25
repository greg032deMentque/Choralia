using System.Net;
using ChoraleBackEnd.Common.Exceptions;
using Microsoft.AspNetCore.Http;

namespace ChoraleBackEnd.Api.Authorization;

public interface ISpaceContextAccessor
{
    Guid? SpaceId { get; }
    Guid RequireSpaceId();
}

public sealed class SpaceContextAccessor : ISpaceContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SpaceContextAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? SpaceId
    {
        get
        {
            var headerValue = _httpContextAccessor.HttpContext?.Request
                .Headers[SpaceRoleAuthorizationHandler.SpaceIdHeaderName]
                .ToString();
            if (Guid.TryParse(headerValue, out var spaceId))
                return spaceId;

            var fallbackValue = _httpContextAccessor.HttpContext?.Request
                .Headers[SpaceRoleAuthorizationHandler.ChoirIdHeaderName]
                .ToString();
            return Guid.TryParse(fallbackValue, out var choirId) ? choirId : null;
        }
    }

    public Guid RequireSpaceId()
        => SpaceId ?? throw new CustomException(
            HttpStatusCode.BadRequest, "En-tête X-Espace-Id requis ou invalide.");
}
