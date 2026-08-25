namespace ChoraleBackEnd.ViewModels.AdminDashboard;

/// <summary>
/// Chorale <c>Publie</c> ou <c>Annule</c> (chorales realement en activite — un <c>Draft</c>
/// n'a pas encore demarre, une <c>Archive</c> est deja fermee) dont la derniere activite
/// mesuree — <c>MAX(LastActive)</c> de ses membres actifs, replie sur <c>LastConnection</c> puis
/// <c>CreatedAt</c> — remonte a plus de 30 jours. Une chorale sans aucun membre actif n'a pas
/// de mesure et n'est pas comptee ici (elle releve plutot de « non demarre » cote client).
/// </summary>
public sealed class InactiveChoirsKpiViewModel
{
    public int Count { get; set; }
    public List<Guid> ChoirIds { get; set; } = [];
}
