using AutoMapper;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data.Entities;

namespace ChoraleBackEnd.ViewModels.ChoirMembers;

public sealed class ChoirMemberListItemViewModel
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid ChoirId { get; set; }
    public MemberStatusEnum Status { get; set; }
    public string? UserFullName { get; set; }
    public string? UserEmail { get; set; }
    public List<string> Roles { get; set; } = [];
    public Guid? SectionId { get; set; }
    public VoicePartEnum? SectionVoicePart { get; set; }
}

public sealed class ChoirMemberListItemViewModelMappingProfile : Profile
{
    public ChoirMemberListItemViewModelMappingProfile()
    {
        CreateMap<SpaceMember, ChoirMemberListItemViewModel>()
            .ForMember(dest => dest.UserFullName, opt => opt.MapFrom(src =>
                src.User != null
                    ? $"{src.User.Firstname} {src.User.Lastname}".Trim()
                    : null))
            .ForMember(dest => dest.UserEmail, opt => opt.MapFrom(src =>
                src.User != null ? src.User.Email : null))
            .ForMember(dest => dest.Roles, opt => opt.Ignore())
            .ForMember(dest => dest.SectionId, opt => opt.Ignore())
            .ForMember(dest => dest.SectionVoicePart, opt => opt.Ignore());
    }
}
