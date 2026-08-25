using System.ComponentModel.DataAnnotations;

namespace ChoraleBackEnd.ViewModels.Onboarding;

public sealed class RequestMembershipViewModel
{
    [Required]
    [StringLength(9, MinimumLength = 8)]
    public string Code { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Message { get; set; }
}
