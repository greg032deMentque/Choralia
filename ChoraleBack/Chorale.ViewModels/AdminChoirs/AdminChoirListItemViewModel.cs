using AutoMapper;
using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.ViewModels.AdminChoirs;

public sealed class AdminChoirListItemViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public int MemberCount { get; set; }
    public int SongCount { get; set; }
    public int UpcomingEventCount { get; set; }
    public DateTime LastActivityAt { get; set; }

    /// <summary>
    /// Cycle de vie (migration 13). Remplace l'ancien booléen <c>IsArchivee</c>, qui
    /// confondait « archivée » et « supprimée » — les deux passaient par <c>IsDeleted</c>.
    /// </summary>
    public ChoirStatusEnum Status { get; set; }
}

public sealed class AdminChoirListItemViewModelMappingProfile : Profile
{
    public AdminChoirListItemViewModelMappingProfile()
    {
        CreateMap<Data.Entities.Choir, AdminChoirListItemViewModel>()
            .ForMember(dest => dest.ClientName, opt => opt.Ignore())
            .ForMember(dest => dest.MemberCount, opt => opt.Ignore())
            .ForMember(dest => dest.SongCount, opt => opt.Ignore())
            .ForMember(dest => dest.UpcomingEventCount, opt => opt.Ignore())
            .ForMember(dest => dest.LastActivityAt, opt => opt.Ignore());
    }
}
