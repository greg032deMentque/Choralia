using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.Data.Entities;

public sealed class SpaceMember : IAuditable
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid SpaceId { get; set; }
    public Guid? ChoirId { get; set; }
    public MemberStatusEnum Status { get; set; }
    public AttendanceEnum? Presence { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedByUserId { get; set; }

    public User User { get; set; } = null!;
    public Choir? Choir { get; set; }
    public Space Space { get; set; } = null!;
}
