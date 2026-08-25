using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.ViewModels.Events;

public sealed class EventPagedFilterViewModel : PaginateViewModel
{
    public Guid? ChoirId { get; set; }
    public EventTypeEnum? Type { get; set; }

    /// <summary>Filtre sur le statut decide. Les brouillons ne remontent de toute facon
    /// qu'aux gestionnaires — ce filtre affine, il n'ouvre rien.</summary>
    public EventStatusEnum? Status { get; set; }

    /// <summary>
    /// true : events a venir (date de fin effective future) ; false : passes ; null :
    /// tous. Sans ce filtre, « les events a venir » obligeait a tout paginer et trier
    /// cote client.
    /// </summary>
    public bool? Upcoming { get; set; }
}
