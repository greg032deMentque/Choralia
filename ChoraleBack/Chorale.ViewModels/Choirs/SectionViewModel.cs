using AutoMapper;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data.Entities;

namespace ChoraleBackEnd.ViewModels.Choirs;

public sealed class SectionViewModel
{
    public Guid? Id { get; set; }
    public Guid ChoirId { get; set; }
    public VoicePartEnum VoicePart { get; set; }
    public string? SectionLeaderId { get; set; }
    public string? SectionLeaderName { get; set; }
    public List<SectionMemberViewModel> Members { get; set; } = [];
}

public sealed class SectionViewModelMappingProfile : Profile
{
    public SectionViewModelMappingProfile()
    {
        CreateMap<Section, SectionViewModel>()
            .ForMember(dest => dest.SectionLeaderName, opt => opt.MapFrom(src =>
                src.SectionLeader != null
                    ? $"{src.SectionLeader.Firstname} {src.SectionLeader.Lastname}".Trim()
                    : null))
            .ForMember(dest => dest.Members, opt => opt.Ignore());

        CreateMap<SectionViewModel, Section>()
            .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedByUserId, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedByUserId, opt => opt.Ignore())
            // Aucun appelant ne fait aujourd'hui _mapper.Map(model, section), mais laisser
            // ChoirId mappable arme le meme defaut que sur Song et SongList : le premier
            // update ecrit ainsi deplacerait le pupitre de chorale. Ignore par prevention,
            // verrouille par EntityMappingGuardTests.
            .ForMember(dest => dest.ChoirId, opt => opt.Ignore())
            .ForMember(dest => dest.Choir, opt => opt.Ignore())
            .ForMember(dest => dest.SectionLeader, opt => opt.Ignore())
            .ForMember(dest => dest.Members, opt => opt.Ignore())
            .ForMember(dest => dest.SongLists, opt => opt.Ignore());
    }
}
