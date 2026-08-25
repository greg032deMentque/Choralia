using System.ComponentModel.DataAnnotations;

namespace ChoraleBackEnd.ViewModels.SongLists;

public sealed class ReorderSongsViewModel
{
    [Required]
    [MaxLength(500)]
    public List<Guid> SongIds { get; set; } = [];
}
