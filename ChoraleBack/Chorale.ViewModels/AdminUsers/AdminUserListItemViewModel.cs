using AutoMapper;
using ChoraleBackEnd.Data.Entities;

namespace ChoraleBackEnd.ViewModels.AdminUsers;

public sealed class AdminUserListItemViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Firstname { get; set; } = string.Empty;
    public string Lastname { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime? LastConnection { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByUserId { get; set; }
    public string? CreatedByName { get; set; }
}

public sealed class AdminUserListItemViewModelMappingProfile : Profile
{
    public AdminUserListItemViewModelMappingProfile()
    {
        CreateMap<User, AdminUserListItemViewModel>()
            .ForMember(dest => dest.Email, opt => opt.MapFrom(src => src.Email ?? string.Empty))
            .ForMember(dest => dest.CreatedByName, opt => opt.Ignore());
    }
}
