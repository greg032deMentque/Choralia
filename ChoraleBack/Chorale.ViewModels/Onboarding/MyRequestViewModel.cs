using AutoMapper;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data.Entities;

namespace ChoraleBackEnd.ViewModels.Onboarding;

/// <summary>
/// Vue demandeur : ses propres demandes. Ne porte jamais <c>DeclineReason</c> — decision produit,
/// le motif de refus reste interne, le demandeur recoit un message neutre.
/// </summary>
public sealed class MyRequestViewModel
{
    public Guid Id { get; set; }
    public Guid SpaceId { get; set; }
    public string SpaceName { get; set; } = string.Empty;
    public MembershipRequestStatusEnum Status { get; set; }
    public string? Message { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class MyRequestViewModelMappingProfile : Profile
{
    public MyRequestViewModelMappingProfile()
    {
        CreateMap<MembershipRequest, MyRequestViewModel>()
            .ForMember(dest => dest.SpaceName, opt => opt.Ignore());
    }
}
