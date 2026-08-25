namespace ChoraleBackEnd.Data.Entities;

public sealed class AnalyticLog
{
    public Guid Id { get; set; }
    public DateTime OccurredAt { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? QueryString { get; set; }
    public int StatusCode { get; set; }
    public long DurationMs { get; set; }
    public string? UserId { get; set; }
    public string? IpAddressHash { get; set; }
    public string? UserAgent { get; set; }
    public string TraceId { get; set; } = string.Empty;
    public string? Endpoint { get; set; }
}
