using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ChoraleBackEnd.Api.Data;

public static partial class SeedDatabase
{
    public static async Task InitializeAsync(IServiceProvider rootProvider, IConfiguration config)
    {
        using var scope = rootProvider.CreateScope();
        var sp = scope.ServiceProvider;

        var logger = sp.GetRequiredService<ILogService>();
        var context = sp.GetRequiredService<ChoraleDbContext>();
        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = sp.GetRequiredService<UserManager<User>>();
        var environment = sp.GetRequiredService<IWebHostEnvironment>();
        var pathService = sp.GetRequiredService<IPathService>();

        // IsRelational() ecarte uniquement le provider InMemory utilise par les tests
        // (ChoraleBackEnd.Test) : en production comme en developpement, SqlServer est toujours
        // relationnel et les migrations s'appliquent exactement comme avant.
        if (context.Database.IsRelational())
        {
            // GetPendingMigrationsAsync interroge __EFMigrationsHistory : sur une base encore
            // inexistante il leverait. CanConnectAsync distingue les deux cas sans exception.
            if (await context.Database.CanConnectAsync())
            {
                var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();
                if (pending.Count == 0)
                    logger.LogInformation("Seed migrations: schema up to date");
                else
                    logger.LogInformation(
                        "Seed migrations: applying {Count} ({Migrations})",
                        pending.Count, string.Join(", ", pending));
            }
            else
            {
                logger.LogInformation("Seed migrations: database absent, full creation");
            }

            await context.Database.MigrateAsync();
        }

        // Section `Seed` liee une seule fois : le super admin et le jeu de demonstration
        // y puisent tous deux leurs donnees.
        var seed = config.GetSection(SeedOptions.SectionName).Get<SeedOptions>();

        await EnsureRolesAsync(roleManager, logger);
        await EnsureSuperAdminAsync(userManager, seed?.Admin, logger);

        // Sans ces donnees, l'application n'est pilotable que par API — l'admin est un
        // operateur hors chorale (`02`), aucun ecran d'affectation n'existe encore, et une
        // base vierge ne contient ni client ni chorale ni membre. Toujours actif en
        // Development (comportement inchange, seul le mot de passe conditionne l'activation).
        // En Staging, un second garde-fou explicite (Seed:Demo:EnabledInStaging) est requis
        // EN PLUS du mot de passe : sans lui, la seule presence du mot de passe sur l'App
        // Service (configure pour une autre raison, ou copie par erreur depuis Development)
        // suffirait a peupler Staging de comptes de demonstration. Jamais actif en Production
        // — IsStaging() y est toujours faux, quels que soient les flags. Un echec de ce seed
        // de confort ne doit jamais empecher l'API de start : il est intercepte et degrade en
        // avertissement, contrairement au seed du super-admin (EnsureSuperAdminAsync) qui
        // reste bloquant.
        var isStagingSeedEnabled = environment.IsStaging() && (seed?.Demo?.EnabledInStaging ?? false);
        if (environment.IsDevelopment() || isStagingSeedEnabled)
        {
            try
            {
                await EnsureDemoDataAsync(context, userManager, environment, pathService, seed?.Demo, logger);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Seed demo failed, application startup continues", ex);
            }
        }
        else if (environment.IsStaging())
        {
            // Diagnostic explicite : sans ce log, l'absence de jeu de demonstration en
            // Staging n'a aucune trace observable — indiscernable d'un `Seed:Demo:Password`
            // simplement absent (cas normal en Development).
            logger.LogInformation(
                "Seed demo skipped: environment is Staging but 'Seed:Demo:EnabledInStaging' is not set to true");
        }
    }
}
