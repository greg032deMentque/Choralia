using AutoMapper;
using ChoraleBackEnd.Data.Entities;

namespace ChoraleBackEnd.ViewModels.Users;

public sealed class UserViewModel
{
    public string? Id { get; set; }
    public string Firstname { get; set; } = string.Empty;
    public string Lastname { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Password { get; set; }
    public List<string> Roles { get; set; } = [];
}

public sealed class UserViewModelMappingProfile : Profile
{
    public UserViewModelMappingProfile()
    {
        CreateMap<User, UserViewModel>()
            .ForMember(dest => dest.Password, opt => opt.Ignore())
            .ForMember(dest => dest.Roles, opt => opt.Ignore());

        CreateMap<UserViewModel, User>()
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedByUserId, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedByUserId, opt => opt.Ignore());
    }
}
