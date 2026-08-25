using AutoMapper;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data.Entities;

namespace ChoraleBackEnd.ViewModels.Events;

public sealed class EventParticipantListItemViewModel
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string? UserFullName { get; set; }
    public string? UserEmail { get; set; }
    public MemberStatusEnum Status { get; set; }
    public AttendanceEnum? Presence { get; set; }
    public List<string> Roles { get; set; } = [];
}

public sealed class EventParticipantListItemViewModelMappingProfile : Profile
{
    public EventParticipantListItemViewModelMappingProfile()
    {
        CreateMap<SpaceMember, EventParticipantListItemViewModel>()
            .ForMember(dest => dest.UserFullName, opt => opt.MapFrom(src =>
                src.User != null
                    ? $"{src.User.Firstname} {src.User.Lastname}".Trim()
                    : null))
            .ForMember(dest => dest.UserEmail, opt => opt.MapFrom(src =>
                src.User != null ? src.User.Email : null))
            .ForMember(dest => dest.Roles, opt => opt.Ignore());
    }
}
