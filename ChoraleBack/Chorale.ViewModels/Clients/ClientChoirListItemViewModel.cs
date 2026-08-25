using AutoMapper;

namespace ChoraleBackEnd.ViewModels.Clients;

/// <summary>
/// Chorale d'un client, avec son niveau d'consommation — ecran central de la zone « Ma structure »
/// du <c>ManagerClient</c> (`10-D23`). Ne porte pas <c>ClientId</c> : il est deja connu,
/// c'est le parametre de route.
/// </summary>
public sealed class ClientChoirListItemViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int MemberCount { get; set; }
    public int SongCount { get; set; }
    public int UpcomingEventCount { get; set; }
}

public sealed class ClientChoirListItemViewModelMappingProfile : Profile
{
    public ClientChoirListItemViewModelMappingProfile()
    {
        CreateMap<Data.Entities.Choir, ClientChoirListItemViewModel>()
            .ForMember(dest => dest.MemberCount, opt => opt.Ignore())
            .ForMember(dest => dest.SongCount, opt => opt.Ignore())
            .ForMember(dest => dest.UpcomingEventCount, opt => opt.Ignore());
    }
}
