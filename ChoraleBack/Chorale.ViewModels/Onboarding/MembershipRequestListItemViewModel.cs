using AutoMapper;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data.Entities;

namespace ChoraleBackEnd.ViewModels.Onboarding;

/// <summary>Vue Responsable de l'espace : file de demandes a traiter.</summary>
public sealed class MembershipRequestListItemViewModel
{
    public Guid Id { get; set; }
    public Guid SpaceId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserFullName { get; set; } = string.Empty;
    public string? UserEmail { get; set; }
    public MembershipRequestStatusEnum Status { get; set; }
    public string? Message { get; set; }
    public string? DeclineReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? HandledAt { get; set; }
}

public sealed class MembershipRequestListItemViewModelMappingProfile : Profile
{
    public MembershipRequestListItemViewModelMappingProfile()
    {
        CreateMap<MembershipRequest, MembershipRequestListItemViewModel>()
            .ForMember(dest => dest.UserFullName,
                opt => opt.MapFrom(src => $"{src.User.Firstname} {src.User.Lastname}".Trim()))
            .ForMember(dest => dest.UserEmail, opt => opt.MapFrom(src => src.User.Email));
    }
}
