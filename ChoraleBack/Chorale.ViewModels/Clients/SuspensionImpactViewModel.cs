namespace ChoraleBackEnd.ViewModels.Clients;

/// <summary>
/// Consequences d'une suspension, presentees avant confirmation (`08` § Clients) : un
/// operateur ne doit pas decouvrir apres coup combien de monde il vient de bloquer.
/// Pendant cote chorale : <c>AdminChoirs.AdminChoirImpactViewModel</c>.
/// </summary>
public sealed class SuspensionImpactViewModel
{
    public int ChoirCount { get; set; }
    public int MemberCount { get; set; }
}
