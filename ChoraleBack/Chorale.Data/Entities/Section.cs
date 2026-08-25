using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.Data.Entities;

public sealed class Section : IAuditable
{
    public Guid Id { get; set; }
    public Guid ChoirId { get; set; }
    public VoicePartEnum VoicePart { get; set; }
    public string? SectionLeaderId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedByUserId { get; set; }

    public Choir Choir { get; set; } = null!;
    public User? SectionLeader { get; set; }
    public ICollection<SectionMember> Members { get; set; } = [];
    public ICollection<SongList> SongLists { get; set; } = [];
}
