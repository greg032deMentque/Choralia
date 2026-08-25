using System.ComponentModel.DataAnnotations;
using AutoMapper;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Helpers;
using ChoraleBackEnd.Data.Entities;

namespace ChoraleBackEnd.ViewModels.Events;

public sealed class EventViewModel
{
    public Guid? Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    [Required]
    public DateTime StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    [Required]
    [EnumDataType(typeof(EventTypeEnum))]
    public EventTypeEnum Type { get; set; }

    [MaxLength(300)]
    public string Location { get; set; } = string.Empty;

    /// <summary>Statut decide. En lecture seule ici : il change via ChangeStatus.</summary>
    public EventStatusEnum Status { get; set; }

    /// <summary>
    /// Etat reellement affichable, dates comprises. C'est cette valeur que l'interface
    /// presente ; <see cref="Status"/> est la decision, pas l'etat.
    /// </summary>
    public EventEffectiveStateEnum EffectiveState { get; set; }

    /// <summary>
    /// Rattachement figé (decision produit) : se decide exclusivement a la creation, via
    /// <c>EventService.CreateAsync</c>, qui le pose explicitement en code apres
    /// verification d'appartenance. Ignore en mapping retour (voir le profil ci-dessous)
    /// pour qu'un appel a <c>EventService.UpdateAsync</c> ne puisse jamais rattacher un
    /// evenement autonome a une chorale, ni le deplacer vers une autre.
    /// </summary>
    public Guid? ChoirId { get; set; }

    /// <summary>
    /// Client de rattachement d'un evenement <b>autonome</b> (`10-D23`) : requis quand
    /// <see cref="ChoirId"/> est absent, ignore sinon — un evenement rattache a une
    /// chorale herite du client de cette chorale. Porte par <c>Space</c>, pas par
    /// l'evenement lui-meme : voir <c>EventService.CreateAsync</c>.
    /// </summary>
    public Guid? ClientId { get; set; }

    public DateTime? ClosedAt { get; set; }
}

public sealed class EventViewModelMappingProfile : Profile
{
    public EventViewModelMappingProfile()
    {
        CreateMap<Event, EventViewModel>()
            .ForMember(dest => dest.EffectiveState, opt => opt.MapFrom(src =>
                EventStateHelper.EffectiveStatus(
                    src.Status, src.StartDate, src.EndDate)));
        CreateMap<EventViewModel, Event>()
            // ChoirId n'est JAMAIS mappe depuis ce DTO : Update ne doit pas pouvoir
            // rattacher ni deplacer un evenement vers une chorale. CreateAsync le pose
            // explicitement en code, apres verification d'appartenance.
            .ForMember(dest => dest.ChoirId, opt => opt.Ignore())
            .ForMember(dest => dest.ClosedAt, opt => opt.Ignore())
            .ForMember(dest => dest.Status, opt => opt.Ignore())
            .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedByUserId, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedByUserId, opt => opt.Ignore())
            .ForMember(dest => dest.Choir, opt => opt.Ignore());
    }
}
