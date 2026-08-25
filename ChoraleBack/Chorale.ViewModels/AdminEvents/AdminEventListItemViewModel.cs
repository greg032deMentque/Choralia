using AutoMapper;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Helpers;
using ChoraleBackEnd.Data.Entities;

namespace ChoraleBackEnd.ViewModels.AdminEvents;

public sealed class AdminEventListItemViewModel
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public EventTypeEnum Type { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string Location { get; set; } = string.Empty;

    /// <summary>Statut decide. En lecture seule ici.</summary>
    public EventStatusEnum Status { get; set; }

    /// <summary>Etat reellement affichable, dates comprises — jamais lu depuis un champ stocke.</summary>
    public EventEffectiveStateEnum EffectiveState { get; set; }

    /// <summary>Chorale porteuse — nullable : un evenement autonome n'en a pas.</summary>
    public Guid? ChoirId { get; set; }
    public string? ChoirName { get; set; }

    public Guid ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;

    public int ParticipantCount { get; set; }

    /// <summary>
    /// true si l'evenement est rattache au client technique cree par la migration
    /// <c>AjouteClientSurSpace</c> pour les events autonomes preexistants sans client
    /// derivable : signale un rattachement manuel a faire par l'exploitation.
    /// </summary>
    public bool IsTechnicalClientAnomaly { get; set; }
}

public sealed class AdminEventListItemViewModelMappingProfile : Profile
{
    public AdminEventListItemViewModelMappingProfile()
    {
        CreateMap<Event, AdminEventListItemViewModel>()
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
