using AutoMapper;
using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.ViewModels.Clients;

/// <summary>
/// Détail d'une chorale du client, écran de fiche de la zone « Ma structure » du
/// <c>ManagerClient</c> (`10-D23`). Ne porte pas <c>ClientId</c> : il est déjà connu, c'est le
/// paramètre de route. Contrairement à <see cref="ClientChoirListItemViewModel"/>, expose
/// <c>Status</c> — nécessaire à l'écran de fiche pour piloter le changement de statut.
/// </summary>
public sealed class ClientChoirDetailViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ChoirStatusEnum Status { get; set; }
    public int MemberCount { get; set; }
    public int SongCount { get; set; }
    public int UpcomingEventCount { get; set; }
}

public sealed class ClientChoirDetailViewModelMappingProfile : Profile
{
    public ClientChoirDetailViewModelMappingProfile()
    {
        CreateMap<Data.Entities.Choir, ClientChoirDetailViewModel>()
            .ForMember(dest => dest.MemberCount, opt => opt.Ignore())
            .ForMember(dest => dest.SongCount, opt => opt.Ignore())
            .ForMember(dest => dest.UpcomingEventCount, opt => opt.Ignore());
    }
}
