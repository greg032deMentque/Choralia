namespace ChoraleBackEnd.ViewModels.AdminDashboard;

/// <summary>
/// Non actionnable en l'etat : la liste d'administration des users
/// (<c>AdminUserController.GetPaged</c>) n'expose aujourd'hui aucun filtre serveur sur
/// <c>IsActive</c>/<c>IsGuestAccount</c> — contrat gele pour ce lot. Les deux champs sont le
/// nom exact des proprietes a filtrer le jour ou cet endpoint les supportera.
/// </summary>
public sealed class UsersKpiViewModel
{
    public int Total { get; set; }
    public int Active { get; set; }

    /// <summary>Compte invite (<c>IsGuestAccount</c>) dont l'email n'est pas confirme — meme definition que le candidat a la purge RGPD.</summary>
    public int InactiveInvitees { get; set; }
}
