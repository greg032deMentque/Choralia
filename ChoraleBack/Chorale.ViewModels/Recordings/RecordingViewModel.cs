using AutoMapper;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data.Entities;

namespace ChoraleBackEnd.ViewModels.Recordings;

public sealed class RecordingViewModel
{
    public Guid? Id { get; set; }
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
    public string? OriginalFileName { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class RecordingViewModelMappingProfile : Profile
{
    public RecordingViewModelMappingProfile()
    {
        CreateMap<Recording, RecordingViewModel>();
    }
}
