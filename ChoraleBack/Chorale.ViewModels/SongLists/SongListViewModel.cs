using System.ComponentModel.DataAnnotations;
using AutoMapper;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data.Entities;

namespace ChoraleBackEnd.ViewModels.SongLists;

public sealed class SongListViewModel
{
    public Guid? Id { get; set; }

    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public Guid? ChoirId { get; set; }
    public Guid? SectionId { get; set; }
    public Guid? EventId { get; set; }
    public string? CreatedById { get; set; }
    public string? OwnerUserId { get; set; }

    [Required]
    [EnumDataType(typeof(SongListTypeEnum))]
    public SongListTypeEnum Type { get; set; }

    public SongListStatusEnum Status { get; set; }

    public List<SongListSongViewModel> Songs { get; set; } = [];
}

public sealed class SongListViewModelMappingProfile : Profile
{
    public SongListViewModelMappingProfile()
    {
        CreateMap<SongList, SongListViewModel>()
            .ForMember(dest => dest.Songs, opt => opt.Ignore());

        CreateMap<SongListViewModel, SongList>()
            .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedByUserId, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedByUserId, opt => opt.Ignore())
            .ForMember(dest => dest.Status, opt => opt.Ignore())
            .ForMember(dest => dest.OwnerUserId, opt => opt.Ignore())
            // Les trois cles de rattachement ne sont jamais reprises du corps de la requete.
            // Dans UpdateAsync, EnsureModificationAsync et EnsureWriteChoirAsync s'executent
            // sur l'entite STOCKEE, et ValidateMembershipAsync ne verifie que la coherence
            // structurelle — jamais l'appartenance a la cible. Un mapping permettait donc de
            // repointer une liste vers n'importe quelle chorale, n'importe quel pupitre ou
            // n'importe quel evenement existant. CreateAsync les pose explicitement.
            .ForMember(dest => dest.ChoirId, opt => opt.Ignore())
            .ForMember(dest => dest.SectionId, opt => opt.Ignore())
            .ForMember(dest => dest.EventId, opt => opt.Ignore())
            .ForMember(dest => dest.Choir, opt => opt.Ignore())
            .ForMember(dest => dest.Section, opt => opt.Ignore())
            .ForMember(dest => dest.Event, opt => opt.Ignore())
            .ForMember(dest => dest.Owner, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
            .ForMember(dest => dest.SongListSongs, opt => opt.Ignore());
    }
}
