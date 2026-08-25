using System.ComponentModel.DataAnnotations;

namespace ChoraleBackEnd.ViewModels.Auth;

public sealed class LoginViewModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    public string? DeviceId { get; set; }
}
