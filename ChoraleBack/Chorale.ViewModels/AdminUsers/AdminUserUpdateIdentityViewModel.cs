using System.ComponentModel.DataAnnotations;

namespace ChoraleBackEnd.ViewModels.AdminUsers;

public sealed class AdminUserUpdateIdentityViewModel
{
    [Required]
    public string Id { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Firstname { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Lastname { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;
}
