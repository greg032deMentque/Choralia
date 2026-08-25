using System.ComponentModel.DataAnnotations;

namespace ChoraleBackEnd.ViewModels.Choirs;

public sealed class UpdateSectionLeaderViewModel
{
    [Required]
    public string SectionLeaderId { get; set; } = string.Empty;
}
