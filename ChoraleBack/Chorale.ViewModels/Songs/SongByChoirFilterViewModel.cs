using System.ComponentModel.DataAnnotations;

namespace ChoraleBackEnd.ViewModels.Songs;

public sealed class SongByChoirFilterViewModel : PaginateViewModel
{
    [Required]
    public Guid ChoirId { get; set; }
}
