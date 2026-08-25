using ChoraleBackEnd.Services;

namespace ChoraleBackEnd.Test.Fakes;

public sealed class FakeLogService : ILogService
{
    public List<string> Warnings { get; } = [];
    public List<string> Informations { get; } = [];
    public List<string> Errors { get; } = [];

    public void LogError(string message) => Errors.Add(message);

    public void LogError(string customMessage, Exception ex, string functionName = "")
        => Errors.Add($"{customMessage} :: {ex.Message}");

    public void LogError(Exception ex, string functionName = "") => Errors.Add(ex.Message);

    public void LogInformation(string message) => Informations.Add(message);

    public void LogWarning(string message) => Warnings.Add(message);

    public void LogWarning(string customMessage, Exception ex, string functionName = "")
        => Warnings.Add($"{customMessage} :: {ex.Message}");

    public void LogDebug(string message)
    {
    }

    public void LogWarning(string messageTemplate, params object[] propertyValues)
        => Warnings.Add(messageTemplate);

    public void LogInformation(string messageTemplate, params object[] propertyValues)
        => Informations.Add(messageTemplate);

    public void LogDebug(string messageTemplate, params object[] propertyValues)
    {
    }

    public void LogError(string messageTemplate, params object[] propertyValues)
        => Errors.Add(messageTemplate);
}
