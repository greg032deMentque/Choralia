using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.Data.Entities;

public sealed class Song : IAuditable
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public SongStatusEnum Status { get; set; }
    public string? Author { get; set; }
    public string? Composer { get; set; }
    public string? Language { get; set; }
    public int? ApproximateDurationSeconds { get; set; }
    public string? WorkingKey { get; set; }
    public SongPriorityEnum? Priority { get; set; }
    public string? PreparationNotes { get; set; }
    public Guid ChoirId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedByUserId { get; set; }

    public Choir Choir { get; set; } = null!;
    public ICollection<SongVoicePart> SongVoicePart { get; set; } = [];
    public ICollection<Score> Scores { get; set; } = [];
    public ICollection<Recording> Recordings { get; set; } = [];
    public ICollection<SongListSong> SongListSongs { get; set; } = [];
}
