namespace ChoraleBackEnd.ViewModels.AdminDashboard;

/// <summary>
/// Events rattaches au client technique « Sans structure » cree par la migration
/// <c>AjouteClientSurSpace</c> (migration 12) pour les events autonomes preexistants sans
/// client derivable — a rattacher manuellement par l'exploitation.
/// </summary>
public sealed class EventsWithoutStructureAnomalyViewModel
{
    public int Count { get; set; }
    public List<Guid> EventIds { get; set; } = [];
}
