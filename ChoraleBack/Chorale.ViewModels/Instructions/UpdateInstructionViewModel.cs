using System.ComponentModel.DataAnnotations;

namespace ChoraleBackEnd.ViewModels.Instructions;

public sealed class UpdateInstructionViewModel
{
    [Required]
    public Guid Id { get; set; }

    [MaxLength(200)]
    public string? Title { get; set; }

    [Required]
    public string Content { get; set; } = string.Empty;
}
