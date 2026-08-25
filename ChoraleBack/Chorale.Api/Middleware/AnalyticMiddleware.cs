using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;

namespace ChoraleBackEnd.Api.Middleware;

public sealed class AnalyticMiddleware
{
    private static readonly HashSet<string> ExcludedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health", "/favicon.ico", "/swagger", "/swagger/index.html", "/metrics"
    };

    // Routes dont la query string porte un secret utilisable : le jeton de verification
    // d'email transite en clair sur GET /api/auth/VerifyEmail et reste valide 24 h. Journalise
    // dans AnalyticLogs.QueryString, il y survivrait sans purge, lisible par quiconque accede
    // a la table. On garde la trace de l'appel (chemin, duree, statut) mais jamais ses parametres.
    private static readonly string[] SensitiveQueryStringPaths = ["/api/auth"];

    private readonly RequestDelegate _next;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _ipSalt;

    public AnalyticMiddleware(RequestDelegate next, IServiceScopeFactory scopeFactory, IConfiguration configuration)
    {
        _next         = next;
        _scopeFactory = scopeFactory;
        _ipSalt       = configuration["Analytics:IpSalt"] ?? "";
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (ShouldSkip(context))
        {
            await _next(context);
            return;
        }

        var startMs = Stopwatch.GetTimestamp();
        await _next(context);
        var durationMs = (long)Stopwatch.GetElapsedTime(startMs).TotalMilliseconds;

        var entry = BuildEntry(context, durationMs);
        _ = PersistAsync(entry);
    }

    private static bool ShouldSkip(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        return ExcludedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    private static bool CarriesSensitiveQueryString(PathString path)
        => SensitiveQueryStringPaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

    private async Task PersistAsync(AnalyticLog entry)
    {
        using var scope = _scopeFactory.CreateScope();
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<ChoraleDbContext>();
            db.AnalyticLogs.Add(entry);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Fire-and-forget : l'echec ne doit jamais remonter dans la requete de l'appelant,
            // mais une trace analytique perdue en silence rend le trou invisible en exploitation.
            scope.ServiceProvider.GetRequiredService<ILogService>()
                .LogWarning("Perte d'une trace analytique. TraceId: {TraceId}, Erreur: {Error}", entry.TraceId, ex.Message);
        }
    }

    private AnalyticLog BuildEntry(HttpContext context, long durationMs)
    {
        var traceId  = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        var userId   = context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var ip       = context.Connection.RemoteIpAddress?.ToString();
        var endpoint = context.GetEndpoint()?.DisplayName;

        var ipHash = string.IsNullOrWhiteSpace(ip) ? null : HashIp(ip, _ipSalt);

        var qs = context.Request.QueryString.HasValue && !CarriesSensitiveQueryString(context.Request.Path)
            ? context.Request.QueryString.Value?[..Math.Min(
                context.Request.QueryString.Value.Length, 2000)]
            : null;

        var ua = context.Request.Headers.UserAgent.ToString();
        if (ua.Length > 512) ua = ua[..512];

        return new AnalyticLog
        {
            Id            = ChoraleDbContext.NewIdGuid(),
            OccurredAt    = DateTime.UtcNow,
            Method        = context.Request.Method,
            Path          = context.Request.Path.Value ?? "",
            QueryString   = qs,
            StatusCode    = context.Response.StatusCode,
            DurationMs    = durationMs,
            UserId        = userId,
            IpAddressHash = ipHash,
            UserAgent     = string.IsNullOrWhiteSpace(ua) ? null : ua,
            TraceId       = traceId,
            Endpoint      = endpoint
        };
    }

    private static string HashIp(string ip, string salt)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(ip + salt));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
