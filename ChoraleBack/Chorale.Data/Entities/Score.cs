using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.Data.Entities;

public sealed class Score : IAuditable
{
    public Guid Id { get; set; }
    public Guid SongId { get; set; }
    public ScoreTypeEnum Type { get; set; }
    public VoicePartEnum? TargetVoicePart { get; set; }
    public string Version { get; set; } = string.Empty;
    public ScoreStatusEnum Status { get; set; }
    public string OwnerUserId { get; set; } = string.Empty;
    public bool DownloadAllowed { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string? OriginalFileName { get; set; }

    /// <summary>
    /// Taille du fichier stocke. Sans elle, le quota de stockage par client (`10-D23`)
    /// n'est pas calculable : ce serait un plafond decoratif.
    /// </summary>
    public long SizeBytes { get; set; }
    public DateTime? PublishedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedByUserId { get; set; }

    public Song Song { get; set; } = null!;
    public User Owner { get; set; } = null!;
}
