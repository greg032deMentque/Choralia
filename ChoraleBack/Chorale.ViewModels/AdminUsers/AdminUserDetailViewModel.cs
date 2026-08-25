using AutoMapper;
using ChoraleBackEnd.Data.Entities;

namespace ChoraleBackEnd.ViewModels.AdminUsers;

public sealed class AdminUserDetailViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Firstname { get; set; } = string.Empty;
    public string Lastname { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsGuestAccount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastConnection { get; set; }
    public DateTime? LastActive { get; set; }
    public List<AdminChoirUserListItemViewModel> Choirs { get; set; } = [];
    public List<AdminEventUserListItemViewModel> Events { get; set; } = [];
    public List<AdminUserDetailClientItemViewModel> ClientAttachments { get; set; } = [];
}

public sealed class AdminUserDetailViewModelMappingProfile : Profile
{
    public AdminUserDetailViewModelMappingProfile()
    {
        CreateMap<User, AdminUserDetailViewModel>()
            .ForMember(dest => dest.Email, opt => opt.MapFrom(src => src.Email ?? string.Empty))
            .ForMember(dest => dest.Choirs, opt => opt.Ignore())
            .ForMember(dest => dest.Events, opt => opt.Ignore())
            .ForMember(dest => dest.ClientAttachments, opt => opt.Ignore());
    }
}
