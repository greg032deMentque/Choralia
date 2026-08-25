using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Services;

namespace ChoraleBackEnd.Services.UserServices;

public interface IUserRoleDataService
{
    /// <summary>
    /// Roles applicatifs de l'utilisateur, normalises sur les valeurs de
    /// <see cref="UserRoleEnum"/> : un role stocke par Identity mais absent de l'enum
    /// serait un role inconnu du domaine, et <c>Enum.Parse</c> le rejette a la lecture
    /// plutot que de le laisser circuler jusqu'au client.
    /// </summary>
    Task<List<string>> GetUserRolesAsync(string userId);
}

public sealed class UserRoleDataService : BaseService, IUserRoleDataService
{
    public UserRoleDataService(IServiceProvider serviceProvider)
        : base(serviceProvider) { }

    public async Task<List<string>> GetUserRolesAsync(string userId)
    {
        var roleNames = await _userManager.GetRolesAsync(
            await _userManager.FindByIdAsync(userId)
                ?? throw new KeyNotFoundException($"User {userId} not found."));

        return roleNames
            .Select(r => Enum.Parse<UserRoleEnum>(r).ToString())
            .ToList();
    }
}
