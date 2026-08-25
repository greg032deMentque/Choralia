using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Data;

namespace ChoraleBackEnd.Services.Technical;

public interface IAuditLogService
{
    void Record(string action, string entityType, string entityId, string? detail = null);
}

public sealed class AuditLogService : BaseService, IAuditLogService
{
    public AuditLogService(IServiceProvider serviceProvider)
        : base(serviceProvider) { }

    public void Record(string action, string entityType, string entityId, string? detail = null)
    {
        _context.AdminAuditLogs.Add(new AdminAuditLog
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = _currentUserId ?? string.Empty,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Detail = detail,
            OccurredAt = DateTime.UtcNow
        });
    }
}
