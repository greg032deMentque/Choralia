using AutoMapper;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data.Entities;

namespace ChoraleBackEnd.ViewModels.Clients;

public sealed class ClientViewModel
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public ClientStatusEnum Status { get; set; }

    public int ChoirLimit { get; set; }
    public int MemberLimit { get; set; }
    public long StorageQuotaBytes { get; set; }
    public long MaxFileSizeBytes { get; set; }

    /// <summary>
    /// Consommation constatee, en regard des plafonds. Un plafond sans consommation visible
    /// est inexploitable a l'ecran (`08` § Clients) — d'ou sa presence sur le meme modele.
    /// </summary>
    public int ChoirCount { get; set; }
    public int MemberCount { get; set; }
    public long UsedStorageBytes { get; set; }
}

public sealed class ClientViewModelMappingProfile : Profile
{
    public ClientViewModelMappingProfile()
    {
        CreateMap<Client, ClientViewModel>()
            .ForMember(dest => dest.ChoirCount, opt => opt.Ignore())
            .ForMember(dest => dest.MemberCount, opt => opt.Ignore())
            .ForMember(dest => dest.UsedStorageBytes, opt => opt.Ignore());
    }
}
