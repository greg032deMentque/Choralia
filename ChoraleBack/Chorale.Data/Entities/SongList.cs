using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.Data.Entities;

public sealed class SongList : IAuditable
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ChoirId { get; set; }
    public Guid? SectionId { get; set; }
    public Guid? EventId { get; set; }
    public SongListTypeEnum Type { get; set; }
    public SongListStatusEnum Status { get; set; }
    public string OwnerUserId { get; set; } = string.Empty;
    public string? CreatedById { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedByUserId { get; set; }

    public Choir? Choir { get; set; }
    public Section? Section { get; set; }
    public Event? Event { get; set; }
    public User Owner { get; set; } = null!;
    public User? CreatedBy { get; set; }
    public ICollection<SongListSong> SongListSongs { get; set; } = [];
}
