namespace ChoraleBackEnd.ViewModels.AdminSongs;

public sealed class AdminSongCatalogPagedFilterViewModel : PaginateViewModel
{
    /// <summary>Ne retourne que les groupes portes par plus d'une chorale.</summary>
    public bool? DuplicatesOnly { get; set; }
}
