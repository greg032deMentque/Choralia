using System.ComponentModel.DataAnnotations;

namespace ChoraleBackEnd.ViewModels.Choirs;

public sealed class AddMemberViewModel
{
    [Required]
    public string UserId { get; set; } = string.Empty;
}
