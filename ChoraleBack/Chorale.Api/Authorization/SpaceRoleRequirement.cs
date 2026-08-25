using ChoraleBackEnd.Common.Enums;
using Microsoft.AspNetCore.Authorization;

namespace ChoraleBackEnd.Api.Authorization;

public sealed class SpaceRoleRequirement : IAuthorizationRequirement
{
    public UserRoleEnum[] AllowedRoles { get; }

    public SpaceRoleRequirement(params UserRoleEnum[] allowedRoles)
    {
        AllowedRoles = allowedRoles;
    }
}
