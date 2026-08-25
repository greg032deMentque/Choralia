using System.ComponentModel.DataAnnotations;

namespace ChoraleBackEnd.ViewModels.Scores;

public sealed class ScoreBySongFilterViewModel : PaginateViewModel
{
    [Required]
    public Guid SongId { get; set; }
}
