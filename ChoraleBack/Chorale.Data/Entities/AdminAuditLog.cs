namespace ChoraleBackEnd.Data.Entities;

public sealed class AdminAuditLog
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? Detail { get; set; }
    public DateTime OccurredAt { get; set; }
}
