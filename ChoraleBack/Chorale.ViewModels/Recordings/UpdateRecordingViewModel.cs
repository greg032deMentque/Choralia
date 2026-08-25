using System.ComponentModel.DataAnnotations;

namespace ChoraleBackEnd.ViewModels.Recordings;

public sealed class UpdateRecordingViewModel
{
    [Required]
    [MaxLength(200)]
    public string ContentOwner { get; set; } = string.Empty;

    public bool DownloadAllowed { get; set; }

    [Required]
    [Range(1, int.MaxValue)]
    public int DurationSeconds { get; set; }
}
