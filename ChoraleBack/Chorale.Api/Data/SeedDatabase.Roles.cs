using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using Microsoft.AspNetCore.Identity;

namespace ChoraleBackEnd.Api.Data;

public static partial class SeedDatabase
{
    private static async Task EnsureRolesAsync(RoleManager<IdentityRole> roleManager, ILogService logger)
    {
        var created = 0;
        var existing = 0;

        foreach (var role in Enum.GetValues<UserRoleEnum>())
        {
            var name = role.ToString();
            if (await roleManager.RoleExistsAsync(name))
            {
                existing++;
                continue;
            }

            var result = await roleManager.CreateAsync(new IdentityRole(name));
            if (!result.Succeeded)
                throw new InvalidOperationException(FormatErrors(result));

            created++;
        }

        // Recapitulatif unique plutot qu'une ligne par role : au premier demarrage d'une base
        // vierge, le detail produisait autant de lignes qu'il y a de valeurs dans UserRoleEnum.
        logger.LogInformation(
            "Seed roles: {Created} created, {Existing} already present", created, existing);
    }

    private static async Task EnsureSuperAdminAsync(
        UserManager<User> userManager, AdminSeedOptions? options, ILogService logger)
    {
        var email = options?.Email?.Trim();
        var password = options?.Password?.Trim();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "Seed super admin skipped: '{EmailKey}' or '{PasswordKey}' missing",
                AdminSeedOptions.EmailKey, AdminSeedOptions.PasswordKey);
            return;
        }

        var adminRole = UserRoleEnum.Admin.ToString();
        var user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            user = new User
            {
                Email = email,
                UserName = email,
                EmailConfirmed = true,
                Firstname = options!.Firstname ?? string.Empty,
                Lastname = options.Lastname ?? string.Empty,
                IsActive = true,
                IsDeleted = false,
                LastActive = DateTime.UtcNow
            };

            // Meme chemin de creation que EnsureDemoDataAsync (CreateAsync(user, password)) :
            // la politique de mot de passe s'applique ici comme partout ailleurs, sans contournement.
            // Un echec est bloquant — start sans admin creerait un environnement injoignable
            // sans aucune trace visible, pire que l'arret explicite ci-dessous.
            var created = await userManager.CreateAsync(user, password);
            if (!created.Succeeded)
                throw new InvalidOperationException(
                    $"Seed super admin impossible : la valeur configuree pour '{AdminSeedOptions.PasswordKey}' " +
                    $"ne respecte pas la politique de mot de passe ({FormatErrors(created)}). Corrigez " +
                    $"la cle de configuration '{AdminSeedOptions.PasswordKey}' (8 caracteres minimum, au " +
                    "moins une majuscule, une minuscule, un chiffre et un caractere non alphanumerique).");

            logger.LogInformation("Seed super admin created: {Email}", email);
        }
        else
        {
            logger.LogInformation("Seed super admin already present: {Email}", email);
        }

        if (!await userManager.IsInRoleAsync(user, adminRole))
        {
            var addRole = await userManager.AddToRoleAsync(user, adminRole);
            if (!addRole.Succeeded)
                throw new InvalidOperationException(FormatErrors(addRole));
        }
    }

    private static string FormatErrors(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(e => $"{e.Code}:{e.Description}"));
}
