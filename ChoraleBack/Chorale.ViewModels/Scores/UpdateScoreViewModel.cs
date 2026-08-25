using System.ComponentModel.DataAnnotations;

namespace ChoraleBackEnd.ViewModels.Scores;

public sealed class UpdateScoreViewModel
{
    [Required]
    [MaxLength(50)]
    public string Version { get; set; } = string.Empty;

    public bool DownloadAllowed { get; set; }
}
