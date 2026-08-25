namespace ChoraleBackEnd.ViewModels.AdminDashboard;

/// <summary>
/// Client (non archive) dont la consommation depasse 80 % d'au moins un de ses quatre
/// plafonds (<c>ChoirLimit</c>, <c>MemberLimit</c>, <c>StorageQuotaBytes</c>, et — pour
/// <c>MaxFileSizeBytes</c>, qui est un plafond par fichier et non un cumul — la taille du
/// plus gros fichier deja depose). Un plafond a 0 est exclu du calcul pour ce client : il ne
/// doit jamais se traduire par un taux de 100 %.
/// </summary>
public sealed class ClientsNearCapKpiViewModel
{
    public int Count { get; set; }
    public List<Guid> ClientIds { get; set; } = [];
}
