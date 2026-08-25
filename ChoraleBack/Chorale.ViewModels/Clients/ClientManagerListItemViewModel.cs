using AutoMapper;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data.Entities;

namespace ChoraleBackEnd.ViewModels.Clients;

/// <summary>
/// Responsable d'un client, tel qu'affiche par l'ecran de management des responsables
/// (`10-D23`). Ne porte pas <c>ClientId</c> : il est deja connu, c'est le parametre de route
/// — meme convention que <see cref="ClientChoirListItemViewModel"/>.
/// </summary>
public sealed class ClientManagerListItemViewModel
{
    public string UserId { get; set; } = string.Empty;
    public string Firstname { get; set; } = string.Empty;
    public string Lastname { get; set; } = string.Empty;
    public string? Email { get; set; }
    public UserRoleEnum Role { get; set; }
    public DateTime AssignmentDate { get; set; }
}

public sealed class ClientManagerListItemViewModelMappingProfile : Profile
{
    public ClientManagerListItemViewModelMappingProfile()
    {
        CreateMap<ClientMember, ClientManagerListItemViewModel>()
            .ForMember(dest => dest.Firstname, opt => opt.MapFrom(src => src.User.Firstname))
            .ForMember(dest => dest.Lastname, opt => opt.MapFrom(src => src.User.Lastname))
            .ForMember(dest => dest.Email, opt => opt.MapFrom(src => src.User.Email))
            .ForMember(dest => dest.AssignmentDate, opt => opt.MapFrom(src => src.CreatedAt));
    }
}
