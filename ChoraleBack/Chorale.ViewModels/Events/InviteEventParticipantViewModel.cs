using System.ComponentModel.DataAnnotations;

namespace ChoraleBackEnd.ViewModels.Events;

public sealed class InviteEventParticipantViewModel
{
    [Required]
    public Guid EventId { get; set; }

    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Firstname { get; set; }
}
