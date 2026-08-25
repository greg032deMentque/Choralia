using System.ComponentModel.DataAnnotations;

namespace ChoraleBackEnd.ViewModels.ChoirMembers;

public sealed class UpdateChoirMemberViewModel
{
    [Required]
    public Guid Id { get; set; }

    [MaxLength(100)]
    public string? Firstname { get; set; }

    [MaxLength(100)]
    public string? Lastname { get; set; }
}
