using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Interceptors;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace ChoraleBackEnd.Api.Extensions;

public static class PersistenceBuilderExtensions
{
    private const string DefaultDataProtectionKeysPath = "./DataProtection-Keys";
    private const string DefaultDataProtectionApplicationName = "ChoraleHelper";

    public static void ConfigureDatabase(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<AuditSaveChangesInterceptor>();
        builder.Services.AddDbContext<ChoraleDbContext>((sp, options) =>
        {
            var connectionString = builder.Configuration.GetConnectionString("DefaultContext")
                ?? throw new InvalidOperationException("Connection string 'DefaultContext' manquante.");
            // Resilience de connexion : la base Azure SQL cible est en tier serverless avec
            // auto-pause. Son reveil dure 30 a 60 s et se manifeste par des erreurs SQL
            // transitoires (40613, 42108, 42109) que le detecteur d'EF Core reconnait deja.
            // Sans cette option, la migration jouee au demarrage par SeedDatabase echoue et
            // l'API ne boote pas apres une periode d'inactivite. Surcharge sans parametres :
            // les defauts d'EF (6 tentatives, backoff jusqu'a 30 s) couvrent un reveil.
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
                   .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
        });
    }

    public static void ConfigureDataProtection(this WebApplicationBuilder builder)
    {
        // Sans persistance explicite, les clés sont regénérées au démarrage : tout lien
        // d'invitation, d'activation ou de vérification d'email en circulation devient illisible
        // dès le redéploiement suivant.
        var keysPath = builder.Configuration["DataProtection:KeysPath"];
        if (string.IsNullOrWhiteSpace(keysPath))
        {
            Log.Warning("DataProtection:KeysPath absent de la configuration — repli sur {Repli}", DefaultDataProtectionKeysPath);
            keysPath = DefaultDataProtectionKeysPath;
        }

        var applicationName = builder.Configuration["DataProtection:ApplicationName"];
        if (string.IsNullOrWhiteSpace(applicationName))
        {
            Log.Warning("DataProtection:ApplicationName absent de la configuration — repli sur {Repli}", DefaultDataProtectionApplicationName);
            applicationName = DefaultDataProtectionApplicationName;
        }

        var keysDirectory = new DirectoryInfo(Path.GetFullPath(keysPath));
        keysDirectory.Create();

        // Stockage fichier : ne partage rien entre instances. Sur une cible Azure multi-instances,
        // un lien émis par l'instance A serait illisible par l'instance B — le passage à
        // PersistKeysToAzureBlobStorage fait l'objet d'un chantier séparé (le paquet
        // Azure.Extensions.AspNetCore.DataProtection.Blobs n'est volontairement pas installé ici).
        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(keysDirectory)
            .SetApplicationName(applicationName);
    }
}
