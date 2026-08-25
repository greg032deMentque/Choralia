using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.ViewModels.Recordings;

public sealed class RecordingPagedFilterViewModel : PaginateViewModel
{
    public Guid? ChoirId { get; set; }
    public Guid? SongId { get; set; }
    public RecordingTypeEnum? Type { get; set; }
    public VoicePartEnum? TargetVoicePart { get; set; }
    public RecordingStatusEnum? Status { get; set; }
    public RecordingSourceEnum? Source { get; set; }
}
