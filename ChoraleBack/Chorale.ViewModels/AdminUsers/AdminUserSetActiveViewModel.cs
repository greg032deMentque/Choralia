using System.ComponentModel.DataAnnotations;

namespace ChoraleBackEnd.ViewModels.AdminUsers;

public sealed class AdminUserSetActiveViewModel
{
    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required(ErrorMessage = "IsActive est requis.")]
    public bool? IsActive { get; set; }
}
