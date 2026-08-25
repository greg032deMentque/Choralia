using System.ComponentModel.DataAnnotations;

namespace ChoraleBackEnd.ViewModels.Events;

public sealed class EventParticipantsPagedFilterViewModel : PaginateViewModel
{
    [Required]
    public Guid EventId { get; set; }
}
