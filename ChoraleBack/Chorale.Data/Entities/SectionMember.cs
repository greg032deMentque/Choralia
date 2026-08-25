namespace ChoraleBackEnd.Data.Entities;

public sealed class SectionMember : IAuditable
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid SectionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedByUserId { get; set; }

    public User User { get; set; } = null!;
    public Section Section { get; set; } = null!;
}
