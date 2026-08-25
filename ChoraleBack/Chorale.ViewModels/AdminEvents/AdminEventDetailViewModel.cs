using AutoMapper;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Helpers;
using ChoraleBackEnd.Data.Entities;

namespace ChoraleBackEnd.ViewModels.AdminEvents;

public sealed class AdminEventDetailViewModel
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public EventTypeEnum Type { get; set; }
    public string Location { get; set; } = string.Empty;
    public EventStatusEnum Status { get; set; }
    public EventEffectiveStateEnum EffectiveState { get; set; }
    public Guid? ChoirId { get; set; }
    public string? ChoirName { get; set; }
    public Guid ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public int ParticipantCount { get; set; }
    public bool IsTechnicalClientAnomaly { get; set; }
    public DateTime? ClosedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class AdminEventDetailViewModelMappingProfile : Profile
{
    public AdminEventDetailViewModelMappingProfile()
    {
        CreateMap<Event, AdminEventDetailViewModel>()
            .ForMember(dest => dest.EffectiveState, opt => opt.MapFrom(src =>
                EventStateHelper.EffectiveStatus(src.Status, src.StartDate, src.EndDate)))
            .ForMember(dest => dest.ChoirName, opt => opt.MapFrom(src =>
                src.Choir != null ? src.Choir.Name : null))
            .ForMember(dest => dest.ClientId, opt => opt.MapFrom(src => src.Space.ClientId))
            .ForMember(dest => dest.ClientName, opt => opt.MapFrom(src =>
                src.Space.Client != null ? src.Space.Client.Name : string.Empty))
            .ForMember(dest => dest.IsTechnicalClientAnomaly, opt => opt.MapFrom(src =>
                src.Space.ClientId == Client.ClientTechnique.WithoutStructure))
            .ForMember(dest => dest.ParticipantCount, opt => opt.Ignore());
    }
}
