using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.Data.Entities;

public sealed class Recording : IAuditable
{
    public Guid Id { get; set; }
    public Guid SongId { get; set; }
    public RecordingTypeEnum Type { get; set; }
    public VoicePartEnum? TargetVoicePart { get; set; }
    public Guid ChoirOwnerId { get; set; }
    public string CreatorUserId { get; set; } = string.Empty;
    public RecordingStatusEnum Status { get; set; }
    public RecordingSourceEnum Source { get; set; }
    public int DurationSeconds { get; set; }
    public DateTime? PublicationDate { get; set; }
    public string ContentOwner { get; set; } = string.Empty;
    public bool DownloadAllowed { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string? OriginalFileName { get; set; }

    /// <summary>
    /// Taille du fichier stocke. Meme raison que pour <see cref="Score"/> : le quota
    /// de stockage agrege les deux types de contenu.
    /// </summary>
    public long SizeBytes { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedByUserId { get; set; }

    public Song Song { get; set; } = null!;
    public Choir ChoirOwner { get; set; } = null!;
    public User Creator { get; set; } = null!;
}
