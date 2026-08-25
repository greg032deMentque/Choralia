namespace ChoraleBackEnd.ViewModels.AdminSongs;

/// <summary>
/// Une ligne = un groupe d'affichage (lot 4), jamais une ligne = un <c>Song</c> — le meme
/// chant depose par plusieurs chorales ne doit apparaitre qu'une fois dans ce catalogue.
/// </summary>
/// <remarks>
/// <see cref="Key"/> est la cle de regroupement calculee par <c>SongKeyHelper</c>. Elle sert
/// d'identifiant opaque pour <c>AdminSongController.GetGroupChoirs</c> : ce catalogue
/// est un regroupement d'AFFICHAGE, il n'existe aucune entite <c>Oeuvre</c> ni identifiant
/// stable en base a exposer a la place.
/// </remarks>
public sealed class AdminSongCatalogItemViewModel
{
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Titre de reference du groupe : le plus frequent parmi les chants du groupe, ou le
    /// premier par ordre alphabetique en cas d'egalite (decision assumee, aucune spec ne
    /// tranchait ce choix).
    /// </summary>
    public string Title { get; set; } = string.Empty;

    public string? Composer { get; set; }

    /// <summary>Count de chorales distinctes portant ce chant.</summary>
    public int ChoirCount { get; set; }

    /// <summary>Count total d'occurrences (chants) dans le groupe.</summary>
    public int OccurrenceCount { get; set; }
}
