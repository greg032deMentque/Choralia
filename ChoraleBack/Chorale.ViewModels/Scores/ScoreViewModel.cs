using AutoMapper;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data.Entities;

namespace ChoraleBackEnd.ViewModels.Scores;

public sealed class ScoreViewModel
{
    public Guid? Id { get; set; }
    public Guid SongId { get; set; }
    public ScoreTypeEnum Type { get; set; }
    public VoicePartEnum? TargetVoicePart { get; set; }
    public string Version { get; set; } = string.Empty;
    public ScoreStatusEnum Status { get; set; }
    public string OwnerUserId { get; set; } = string.Empty;
    public bool DownloadAllowed { get; set; }
    public string? OriginalFileName { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class ScoreViewModelMappingProfile : Profile
{
    public ScoreViewModelMappingProfile()
    {
        CreateMap<Score, ScoreViewModel>();
    }
}
