using AspNetCoreRateLimit;
using ChoraleBackEnd.Api.Data;
using ChoraleBackEnd.Api.Middleware;
using ChoraleBackEnd.ViewModels.Auth;

namespace ChoraleBackEnd.Api.Extensions;

public static class ApplicationBuilderExtensions
{
    public static void ConfigureApplicationServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddMemoryCache();
        builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
        builder.Services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
        builder.Services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();
        builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
        builder.Services.AddSingleton<IProcessingStrategy, AsyncKeyLockProcessingStrategy>();
        builder.Services.AddInMemoryRateLimiting();

        ProgramServiceDeclarator.ServicesDeclarator(builder);
    }

    public static void ConfigureControllers(this WebApplicationBuilder builder)
    {
        builder.Services.AddControllers()
            .AddNewtonsoftJson(options =>
            {
                options.SerializerSettings.ContractResolver =
                    new Newtonsoft.Json.Serialization.DefaultContractResolver();

                // Pas de StringEnumConverter : les enums circulent en entier, comme ils sont
                // stockes. Le front les typait deja en numerique et devait reconstituer la
                // valeur a partir de la chaine recue, service par service — une couche de
                // traduction entiere qui n'existait que pour compenser ce convertisseur.
            })
            .ConfigureApiBehaviorOptions(options =>
            {
                options.InvalidModelStateResponseFactory = _ => new InvalidModelStateResult();
            });
    }

    public static void ConfigureSwagger(this WebApplicationBuilder builder)
    {
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
    }

    public static void ConfigureAutoMapper(this WebApplicationBuilder builder)
    {
        // La cle ne figure PAS dans appsettings.json : ce fichier est versionne et le depot
        // vitrine GitHub est public. Elle vient d'appsettings.Development.json en local et des
        // Application Settings Azure en production. Quand elle est absente on ne l'affecte pas
        // du tout : passer une chaine vide a LicenseKey n'equivaut pas a ne rien passer.
        var licenseKey = builder.Configuration["AutomapperLicense"];

        builder.Services.AddAutoMapper(
            cfg =>
            {
                if (!string.IsNullOrWhiteSpace(licenseKey))
                    cfg.LicenseKey = licenseKey;
            },
            typeof(LoginViewModel).Assembly // on charge le projet où se trouve le view model. On chargera tous les autres viewmodel avec un Profile
        );
    }
}
