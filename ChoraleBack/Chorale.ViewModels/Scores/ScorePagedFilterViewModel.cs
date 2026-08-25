using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.ViewModels.Scores;

public sealed class ScorePagedFilterViewModel : PaginateViewModel
{
    public Guid? ChoirId { get; set; }
    public Guid? SongId { get; set; }
    public ScoreTypeEnum? Type { get; set; }
    public VoicePartEnum? TargetVoicePart { get; set; }
    public ScoreStatusEnum? Status { get; set; }
}
