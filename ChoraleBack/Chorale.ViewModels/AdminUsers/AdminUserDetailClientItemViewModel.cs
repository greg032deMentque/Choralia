using AutoMapper;
using ChoraleBackEnd.Data.Entities;

namespace ChoraleBackEnd.ViewModels.AdminUsers;

public sealed class AdminUserDetailClientItemViewModel
{
    public Guid ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public sealed class AdminUserDetailClientItemViewModelMappingProfile : Profile
{
    public AdminUserDetailClientItemViewModelMappingProfile()
    {
        CreateMap<ClientMember, AdminUserDetailClientItemViewModel>()
            .ForMember(dest => dest.ClientName, opt => opt.MapFrom(src => src.Client != null ? src.Client.Name : string.Empty))
            .ForMember(dest => dest.Role, opt => opt.MapFrom(src => src.Role.ToString()));
    }
}
