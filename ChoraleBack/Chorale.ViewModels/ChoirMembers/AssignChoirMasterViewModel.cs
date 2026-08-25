using System.ComponentModel.DataAnnotations;

namespace ChoraleBackEnd.ViewModels.ChoirMembers;

public sealed class AssignChoirMasterViewModel
{
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;
}
