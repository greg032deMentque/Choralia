using System.Runtime.CompilerServices;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Serilog;

namespace ChoraleBackEnd.Services;

public interface ILogService
{
    void LogError(string message);
    void LogError(string customMessage, Exception ex, [CallerMemberName] string functionName = "");
    void LogError(Exception ex, [CallerMemberName] string functionName = "");
    void LogInformation(string message);
    void LogWarning(string message);
    void LogWarning(string customMessage, Exception ex, [CallerMemberName] string functionName = "");
    void LogDebug(string message);
    void LogWarning(string messageTemplate, params object[] propertyValues);
    void LogInformation(string messageTemplate, params object[] propertyValues);
    void LogDebug(string messageTemplate, params object[] propertyValues);
    void LogError(string messageTemplate, params object[] propertyValues);
}

public sealed class LogService : ILogService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public LogService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ILogger WithContext(string functionName)
    {
        var context = _httpContextAccessor.HttpContext;
        var userId = context?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "null";
        var traceId = context?.TraceIdentifier ?? "";
        var endpoint = context?.GetEndpoint()?.DisplayName ?? "";
        var rv = context?.Request.RouteValues;
        var controller = rv is null ? "" : rv.TryGetValue("controller", out var c) ? c?.ToString() ?? "" : "";
        var action = rv is null ? "" : rv.TryGetValue("action", out var a) ? a?.ToString() ?? "" : "";
        var path = context?.Request.Path.Value ?? "";
        var method = context?.Request.Method ?? "";

        return Log.ForContext("UserId", userId)
                  .ForContext("TraceId", traceId)
                  .ForContext("Function", functionName)
                  .ForContext("Endpoint", endpoint)
                  .ForContext("Controller", controller)
                  .ForContext("Action", action)
                  .ForContext("Path", path)
                  .ForContext("Method", method);
    }

    public void LogError(string message)
        => WithContext(nameof(LogError)).Error("{Message}", message);

    public void LogError(string customMessage, Exception ex, [CallerMemberName] string functionName = "")
        => WithContext(functionName).Error(ex, "{CustomMessage}", customMessage);

    public void LogError(Exception ex, [CallerMemberName] string functionName = "")
        => WithContext(functionName).Error(ex, "Unhandled exception");

    public void LogWarning(string message)
        => WithContext(nameof(LogWarning)).Warning("{Message}", message);

    public void LogWarning(string customMessage, Exception ex, [CallerMemberName] string functionName = "")
        => WithContext(functionName).Warning(ex, "{CustomMessage}", customMessage);

    public void LogInformation(string message)
        => WithContext(nameof(LogInformation)).Information("{Message}", message);

    public void LogDebug(string message)
        => WithContext(nameof(LogDebug)).Debug("{Message}", message);

    public void LogWarning(string messageTemplate, params object[] propertyValues)
        => LogWarningTemplateCore(messageTemplate, propertyValues);

    public void LogInformation(string messageTemplate, params object[] propertyValues)
        => LogInformationTemplateCore(messageTemplate, propertyValues);

    public void LogDebug(string messageTemplate, params object[] propertyValues)
        => LogDebugTemplateCore(messageTemplate, propertyValues);

    public void LogError(string messageTemplate, params object[] propertyValues)
        => LogErrorTemplateCore(messageTemplate, propertyValues);

    private void LogWarningTemplateCore(string t, object[] v, [CallerMemberName] string fn = "")
        => WithContext(fn).Warning(t, v);

    private void LogInformationTemplateCore(string t, object[] v, [CallerMemberName] string fn = "")
        => WithContext(fn).Information(t, v);

    private void LogDebugTemplateCore(string t, object[] v, [CallerMemberName] string fn = "")
        => WithContext(fn).Debug(t, v);

    private void LogErrorTemplateCore(string t, object[] v, [CallerMemberName] string fn = "")
        => WithContext(fn).Error(t, v);
}
