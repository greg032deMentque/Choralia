using System.ComponentModel.DataAnnotations;
using AutoMapper;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data.Entities;

namespace ChoraleBackEnd.ViewModels.Songs;

public sealed class SongViewModel
{
    public Guid? Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [EnumDataType(typeof(SongStatusEnum))]
    public SongStatusEnum Status { get; set; }

    [Required]
    [MinLength(1)]
    public List<VoicePartEnum> VoiceParts { get; set; } = [];

    [MaxLength(150)]
    public string? Author { get; set; }

    [MaxLength(150)]
    public string? Composer { get; set; }

    [MaxLength(100)]
    public string? Language { get; set; }

    public int? ApproximateDurationSeconds { get; set; }

    [MaxLength(100)]
    public string? WorkingKey { get; set; }

    public SongPriorityEnum? Priority { get; set; }

    [MaxLength(2000)]
    public string? PreparationNotes { get; set; }

    public Guid ChoirId { get; set; }

    public bool IsCompleteForChoir { get; set; }

    public List<VoicePartEnum> VoicePartsWithoutPublishedRecording { get; set; } = [];
}

public sealed class SongViewModelMappingProfile : Profile
{
    public SongViewModelMappingProfile()
    {
        CreateMap<Song, SongViewModel>()
            .ForMember(dest => dest.VoiceParts, opt => opt.MapFrom(src => src.SongVoicePart.Select(cv => cv.VoicePart)))
            .ForMember(dest => dest.IsCompleteForChoir, opt => opt.Ignore())
            .ForMember(dest => dest.VoicePartsWithoutPublishedRecording, opt => opt.Ignore());

        CreateMap<SongViewModel, Song>()
            .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedByUserId, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedByUserId, opt => opt.Ignore())
            // ChoirId n'est jamais repris du corps de la requete. Les gardes de
            // SongService.UpdateAsync s'executent sur la valeur STOCKEE ; un mapping
            // l'ecraserait juste apres, deplacant le chant et tout son contenu vers une
            // autre chorale — y compris d'un autre client. CreateAsync la pose
            // explicitement, une fois model.ChoirId valide par les trois gardes.
            .ForMember(dest => dest.ChoirId, opt => opt.Ignore())
            .ForMember(dest => dest.Choir, opt => opt.Ignore())
            .ForMember(dest => dest.SongVoicePart, opt => opt.Ignore())
            .ForMember(dest => dest.Scores, opt => opt.Ignore())
            .ForMember(dest => dest.Recordings, opt => opt.Ignore())
            .ForMember(dest => dest.SongListSongs, opt => opt.Ignore());
    }
}
