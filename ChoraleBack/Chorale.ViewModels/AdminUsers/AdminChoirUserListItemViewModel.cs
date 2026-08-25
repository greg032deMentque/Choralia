using AutoMapper;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data.Entities;

namespace ChoraleBackEnd.ViewModels.AdminUsers;

public sealed class AdminChoirUserListItemViewModel
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Firstname { get; set; } = string.Empty;
    public string Lastname { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public Guid ChoirId { get; set; }
    public string ChoirName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];
    public VoicePartEnum? PrimaryVoicePart { get; set; }
    public MemberStatusEnum Status { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LastActive { get; set; }
}

public sealed class AdminChoirUserListItemViewModelMappingProfile : Profile
{
    public AdminChoirUserListItemViewModelMappingProfile()
    {
        CreateMap<SpaceMember, AdminChoirUserListItemViewModel>()
            .ForMember(dest => dest.Firstname, opt => opt.MapFrom(src => src.User != null ? src.User.Firstname : string.Empty))
            .ForMember(dest => dest.Lastname, opt => opt.MapFrom(src => src.User != null ? src.User.Lastname : string.Empty))
            .ForMember(dest => dest.Email, opt => opt.MapFrom(src => src.User != null ? src.User.Email ?? string.Empty : string.Empty))
            .ForMember(dest => dest.IsActive, opt => opt.MapFrom(src => src.User != null && src.User.IsActive))
            .ForMember(dest => dest.LastActive, opt => opt.MapFrom(src => src.User != null ? src.User.LastActive : null))
            .ForMember(dest => dest.ChoirId, opt => opt.MapFrom(src => src.ChoirId ?? Guid.Empty))
            .ForMember(dest => dest.ChoirName, opt => opt.MapFrom(src => src.Choir != null ? src.Choir.Name : string.Empty))
            .ForMember(dest => dest.Roles, opt => opt.Ignore())
            .ForMember(dest => dest.PrimaryVoicePart, opt => opt.Ignore());
    }
}
