namespace ChoraleBackEnd.ViewModels.AdminDashboard;

/// <summary>
/// Indicateurs du tableau de bord d'administration generale (`10-D30`).
/// </summary>
/// <remarks>
/// Seuls les indicateurs reellement calculables sur les donnees existantes figurent ici —
/// aucun indicateur financier (impayes, renouvellements, Stripe) : aucune source en base ne
/// les alimente, et un indicateur invente est pire qu'un indicateur absent.
///
/// Chaque sous-objet documente, sur son propre champ, de quoi l'ecran a besoin pour
/// construire le filtre de la liste correspondante quand l'indicateur est cliquable : soit
/// une valeur d'enum a passer en filtre, soit — quand l'ensemble reste court — la liste des
/// identifiants concernes directement.
/// </remarks>
public sealed class AdminDashboardKpiViewModel
{
    public ClientsKpiViewModel Clients { get; set; } = new();
    public ChoirsKpiViewModel Choirs { get; set; } = new();
    public UsersKpiViewModel Users { get; set; } = new();
    public InactiveChoirsKpiViewModel InactiveChoirs { get; set; } = new();
    public NotStartedClientsKpiViewModel NotStartedClients { get; set; } = new();
    public ClientsNearCapKpiViewModel ClientsNearCap { get; set; } = new();

    /// <summary>Volume total de files stockes (partitions et enregistrements confondus, soft-deletes inclus — le disque reste occupe). Non actionnable : aucun ecran de liste ne porte sur ce total agrege.</summary>
    public long TotalStorageBytes { get; set; }

    public SongsKpiViewModel Songs { get; set; } = new();
    public int UpcomingEvents30Days { get; set; }
    public EventsWithoutStructureAnomalyViewModel EventsWithoutStructureAnomaly { get; set; } = new();
}
