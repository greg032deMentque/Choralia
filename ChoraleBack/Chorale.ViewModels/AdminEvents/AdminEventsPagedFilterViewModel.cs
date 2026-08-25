using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.ViewModels.AdminEvents;

public sealed class AdminEventsPagedFilterViewModel : PaginateViewModel
{
    public Guid? ClientId { get; set; }
    public Guid? ChoirId { get; set; }
    public EventStatusEnum? Status { get; set; }
    public EventTypeEnum? Type { get; set; }

    /// <summary>true : a venir ; false : passes ; null : tous.</summary>
    public bool? Upcoming { get; set; }
}
