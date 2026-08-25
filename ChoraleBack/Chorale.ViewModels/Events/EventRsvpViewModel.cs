using System.ComponentModel.DataAnnotations;
using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.ViewModels.Events;

public sealed class EventRsvpViewModel
{
    [Required]
    public Guid EventId { get; set; }

    [Required]
    [EnumDataType(typeof(AttendanceEnum))]
    public AttendanceEnum Presence { get; set; }
}
