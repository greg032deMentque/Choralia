using System.ComponentModel.DataAnnotations;

namespace ChoraleBackEnd.ViewModels.Auth;

public sealed class ResendVerificationViewModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}
