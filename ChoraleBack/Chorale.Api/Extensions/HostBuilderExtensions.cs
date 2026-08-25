using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace ChoraleBackEnd.Api.Extensions;

public static class HostBuilderExtensions
{
    public static void ConfigureLogging(this WebApplicationBuilder builder)
    {
        // Repli "./Logs" si la cle est absente : Path.GetFullPath(null) leverait, et un chemin
        // de logs manquant ne doit pas empecher l'API de start.
        var logPath = builder.Configuration.GetSection("Logs_path").Value;
        if (string.IsNullOrWhiteSpace(logPath))
        {
            Log.Warning("Logs_path absent de la configuration — repli sur ./Logs");
            logPath = "./Logs";
        }

        var baseLogPath = Path.GetFullPath(logPath);

        var logPathAll = Path.Combine(baseLogPath, "LogsDebug", "MediaPJ_Back_.log");
        var logPathError = Path.Combine(baseLogPath, "LogsError", "MediaPJ_Back_error_.log");

        builder.Logging.ClearProviders();

        builder.Host.UseSerilog((ctx, lc) => lc
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            // Reactive les lignes de demarrage de Microsoft.Hosting.Lifetime — « Now listening
            // on », « Application started », environnement, content root — que l'override
            // « Microsoft » a Warning ci-dessus eteignait. Serilog resout par prefixe le plus
            // long : cet override-ci l'emporte.
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss.fff} {Level:u3}] [trace:{TraceId}] [user:{UserId}] {Message:lj} {Properties:j}{NewLine}{Exception}")
            .WriteTo.File(new RenderedCompactJsonFormatter(), logPathAll, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .WriteTo.File(new RenderedCompactJsonFormatter(), logPathError, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7, restrictedToMinimumLevel: LogEventLevel.Error)
        );
    }

    public static void ConfigureKestrel(this WebApplicationBuilder builder)
    {
        const long maxRequestSize = 100 * 1024 * 1024;
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxRequestSize);
    }
}
