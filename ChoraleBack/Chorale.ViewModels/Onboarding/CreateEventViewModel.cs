using System.ComponentModel.DataAnnotations;
using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.ViewModels.Onboarding;

public sealed class CreateEventViewModel
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    [Required]
    public DateTime StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    [Required]
    [EnumDataType(typeof(EventTypeEnum))]
    public EventTypeEnum Type { get; set; }

    [MaxLength(300)]
    public string? Location { get; set; }

    /// <summary>Facultatif : voir <see cref="CreateChoirViewModel.Structure"/>.</summary>
    [MaxLength(150)]
    public string? Structure { get; set; }
}
