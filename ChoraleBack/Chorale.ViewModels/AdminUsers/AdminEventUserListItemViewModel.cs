using AutoMapper;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data.Entities;

namespace ChoraleBackEnd.ViewModels.AdminUsers;

public sealed class AdminEventUserListItemViewModel
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Firstname { get; set; } = string.Empty;
    public string Lastname { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public Guid EventId { get; set; }
    public string EventTitle { get; set; } = string.Empty;
    public DateTime EventStartDate { get; set; }
    public Guid? ChoirId { get; set; }
    public string? ChoirName { get; set; }
    public string Role { get; set; } = string.Empty;
    public AttendanceEnum? Presence { get; set; }
    public MemberStatusEnum Status { get; set; }
}

public sealed class AdminEventUserListItemViewModelMappingProfile : Profile
{
    public AdminEventUserListItemViewModelMappingProfile()
    {
        CreateMap<SpaceMember, AdminEventUserListItemViewModel>()
            .ForMember(dest => dest.Firstname, opt => opt.MapFrom(src => src.User != null ? src.User.Firstname : string.Empty))
            .ForMember(dest => dest.Lastname, opt => opt.MapFrom(src => src.User != null ? src.User.Lastname : string.Empty))
            .ForMember(dest => dest.Email, opt => opt.MapFrom(src => src.User != null ? src.User.Email ?? string.Empty : string.Empty))
            .ForMember(dest => dest.EventId, opt => opt.MapFrom(src => src.SpaceId))
            .ForMember(dest => dest.EventTitle, opt => opt.Ignore())
            .ForMember(dest => dest.EventStartDate, opt => opt.Ignore())
            .ForMember(dest => dest.ChoirId, opt => opt.Ignore())
            .ForMember(dest => dest.ChoirName, opt => opt.Ignore())
            .ForMember(dest => dest.Role, opt => opt.Ignore());
    }
}
