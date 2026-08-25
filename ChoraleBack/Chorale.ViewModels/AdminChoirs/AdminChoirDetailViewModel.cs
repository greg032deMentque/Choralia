using AutoMapper;
using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.ViewModels.AdminChoirs;

public sealed class AdminChoirDetailViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public Guid ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;

    /// <summary>
    /// Cycle de vie (migration 13). Remplace l'ancien booléen <c>IsArchivee</c>.
    /// </summary>
    public ChoirStatusEnum Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public int MemberCount { get; set; }
    public int SongCount { get; set; }
    public int EventCount { get; set; }

    /// <summary>
    /// Consommation du CLIENT en regard de ses plafonds — jamais celle de la seule chorale :
    /// les plafonds portent sur le client (`04` § Client, `10-D23`). Un plafond sans
    /// consommation visible est inexploitable a l'ecran.
    /// </summary>
    public int ClientChoirLimit { get; set; }
    public int ClientChoirCount { get; set; }
    public int ClientMemberLimit { get; set; }
    public int ClientMemberCount { get; set; }
    public long ClientStorageQuotaBytes { get; set; }
    public long ClientUsedStorageBytes { get; set; }
}

public sealed class AdminChoirDetailViewModelMappingProfile : Profile
{
    public AdminChoirDetailViewModelMappingProfile()
    {
        CreateMap<Data.Entities.Choir, AdminChoirDetailViewModel>()
            .ForMember(dest => dest.ClientName, opt => opt.MapFrom(src => src.Client != null ? src.Client.Name : string.Empty))
            .ForMember(dest => dest.MemberCount, opt => opt.Ignore())
            .ForMember(dest => dest.SongCount, opt => opt.Ignore())
            .ForMember(dest => dest.EventCount, opt => opt.Ignore())
            .ForMember(dest => dest.ClientChoirLimit, opt => opt.Ignore())
            .ForMember(dest => dest.ClientChoirCount, opt => opt.Ignore())
            .ForMember(dest => dest.ClientMemberLimit, opt => opt.Ignore())
            .ForMember(dest => dest.ClientMemberCount, opt => opt.Ignore())
            .ForMember(dest => dest.ClientStorageQuotaBytes, opt => opt.Ignore())
            .ForMember(dest => dest.ClientUsedStorageBytes, opt => opt.Ignore());
    }
}
