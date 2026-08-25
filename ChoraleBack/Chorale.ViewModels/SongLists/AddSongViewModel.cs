using System.ComponentModel.DataAnnotations;

namespace ChoraleBackEnd.ViewModels.SongLists;

public sealed class AddSongViewModel
{
    [Required]
    public Guid SongId { get; set; }

    public int Position { get; set; }
}
