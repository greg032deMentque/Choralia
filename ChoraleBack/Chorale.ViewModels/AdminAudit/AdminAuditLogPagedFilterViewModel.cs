namespace ChoraleBackEnd.ViewModels.AdminAudit;

public sealed class AdminAuditLogPagedFilterViewModel : PaginateViewModel
{
    /// <summary>Identifiant de l'acteur (<c>AdminAuditLog.UserId</c>).</summary>
    public string? UserId { get; set; }

    public string? EntityType { get; set; }
    public string? Action { get; set; }

    /// <summary>Borne inferieure (incluse) sur <c>OccurredAt</c>, en UTC.</summary>
    public DateTime? StartDate { get; set; }

    /// <summary>Borne superieure (incluse) sur <c>OccurredAt</c>, en UTC.</summary>
    public DateTime? EndDate { get; set; }
}
