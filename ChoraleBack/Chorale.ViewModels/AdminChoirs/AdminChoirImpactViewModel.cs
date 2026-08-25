namespace ChoraleBackEnd.ViewModels.AdminChoirs;

/// <summary>
/// Consequences d'un archivage, presentees avant confirmation — meme principe que
/// <c>SuspensionImpactViewModel</c> cote client (`08` § Clients) : un operateur ne doit pas
/// decouvrir apres coup combien de contenu il vient de masquer.
/// </summary>
public sealed class AdminChoirImpactViewModel
{
    public int MemberCount { get; set; }
    public int SongCount { get; set; }
    public int EventCount { get; set; }
}
