using System.ComponentModel.DataAnnotations;

namespace ChoraleBackEnd.ViewModels.Recordings;

public sealed class RecordingBySongFilterViewModel : PaginateViewModel
{
    [Required]
    public Guid SongId { get; set; }
}
