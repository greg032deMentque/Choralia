using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.Data.Entities;

public sealed class SpaceMemberRole : IAuditable
{
    public Guid Id { get; set; }
    public Guid SpaceMemberId { get; set; }
    public UserRoleEnum Role { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedByUserId { get; set; }

    public SpaceMember SpaceMember { get; set; } = null!;
}
