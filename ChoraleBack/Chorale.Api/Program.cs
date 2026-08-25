using AspNetCoreRateLimit;
using ChoraleBackEnd.Api.Data;
using ChoraleBackEnd.Api.Extensions;
using ChoraleBackEnd.Api.Middleware;
using Serilog;
using Serilog.Events;
using System.Security.Claims;

// Logger minimal actif avant builder.Build(). Sans lui, tout ce qui avertit ou echoue
// pendant la construction — cle de configuration absente, repli de chemin, secret manquant —
// part dans le SilentLogger de Serilog, donc nulle part. ConfigureLogging() le remplace
// ensuite par la configuration complete (console + fichiers).
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

try
{
    Log.Information("Demarrage — environnement {Environment}", builder.Environment.EnvironmentName);

    builder.ConfigureLogging();
    builder.ConfigureKestrel();
    builder.ConfigureDatabase();
    builder.ConfigureDataProtection();
    builder.ConfigureIdentity();
    builder.ConfigureJwt();
    builder.ConfigureAuthorization();
    builder.ConfigureCors();
    builder.ConfigureApplicationServices();
    builder.ConfigureControllers();
    builder.ConfigureSwagger();
    builder.ConfigureAutoMapper();

    var app = builder.Build();

    await SeedDatabase.InitializeAsync(app.Services, app.Configuration);

    if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
    {
        var swaggerEnabled = builder.Configuration.GetValue<bool>("Swagger:Enabled");
        if (swaggerEnabled)
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }
    }

    // Enregistre APRES le bloc Swagger, et avant tout le reste : la CSP `default-src 'none'` est
    // incompatible avec Swagger UI (HTML a scripts et styles inline), qui court-circuite le
    // pipeline sur ses propres routes et n'est de toute facon monte qu'en Development/Staging. En
    // production ce bloc n'existe pas et ce middleware est donc le premier — necessaire pour que
    // les en-tetes couvrent aussi les reponses qui n'atteignent jamais un controller : 429 du rate
    // limiting, 500 de l'ExceptionMiddleware.
    app.UseMiddleware<SecurityHeadersMiddleware>();

    // Une ligne de synthese par requete (methode, chemin, statut, duree) : sans middleware
    // dedie, aucune trace de navigation n'existe — le code applicatif ne logue que ses erreurs
    // (ExceptionMiddleware), jamais le chemin passant. Place avant IpRateLimiting et
    // ExceptionMiddleware pour que les 429 et les 500 convertis produisent eux aussi leur ligne.
    app.UseSerilogRequestLogging(options =>
    {
        const int ServerErrorStatusThreshold = 500;
        options.GetLevel = (httpContext, elapsed, ex) => ex is not null || httpContext.Response.StatusCode >= ServerErrorStatusThreshold
            ? LogEventLevel.Error
            : LogEventLevel.Information;

        // Memes proprietes que LogService.WithContext (TraceId, UserId) : le gabarit console
        // de ConfigureLogging les affiche deja pour toutes les lignes, cette ligne de synthese
        // ne doit pas y apparaitre vide.
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("TraceId", httpContext.TraceIdentifier);
            diagnosticContext.Set("UserId", httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "null");
        };
    });

    app.UseCors("Frontend");
    app.UseIpRateLimiting();
    app.UseMiddleware<ExceptionMiddleware>();
    app.UseMiddleware<AnalyticMiddleware>();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();

    app.Run();
}
catch (Exception ex)
{
    // ConfigureDatabase et ConfigureJwt levent sur une cle de configuration absente, donc
    // avant builder.Build() et avant l'existence du moindre sink : sans ce catch, une
    // configuration incomplete arrete l'API sans laisser une seule ligne de journal.
    Log.Fatal(ex, "Demarrage interrompu");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
