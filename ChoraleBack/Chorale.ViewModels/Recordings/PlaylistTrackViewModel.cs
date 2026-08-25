using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.ViewModels.Recordings;

public sealed class PlaylistTrackViewModel
{
    public Guid RecordingId { get; set; }
    public Guid SongId { get; set; }
    public string SongTitle { get; set; } = string.Empty;
    public VoicePartEnum? TargetVoicePart { get; set; }
    public int DurationSeconds { get; set; }
    public int Position { get; set; }
}
