using AutoMapper;
using ChoraleBackEnd.Data.Entities;

namespace ChoraleBackEnd.ViewModels.AdminUsers;

public sealed class AdminUnattachedUserListItemViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Firstname { get; set; } = string.Empty;
    public string Lastname { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsGuestAccount { get; set; }
    public string? ClientName { get; set; }
    public string? ClientRole { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastConnection { get; set; }
}

public sealed class AdminUnattachedUserListItemViewModelMappingProfile : Profile
{
    public AdminUnattachedUserListItemViewModelMappingProfile()
    {
        CreateMap<User, AdminUnattachedUserListItemViewModel>()
            .ForMember(dest => dest.Email, opt => opt.MapFrom(src => src.Email ?? string.Empty))
            .ForMember(dest => dest.ClientName, opt => opt.Ignore())
            .ForMember(dest => dest.ClientRole, opt => opt.Ignore());
    }
}
