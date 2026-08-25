using System.ComponentModel.DataAnnotations;
using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.ViewModels.Clients;

/// <summary>
/// Filtre de <c>ClientController.GetPaged</c> (`10-D30`) : les tuiles du tableau de bord
/// d'administration doivent ouvrir la liste des clients deja filtree, sans quoi elles ne
/// peuvent afficher que des identifiants bruts.
/// </summary>
public sealed class ClientsPagedFilterViewModel : PaginateViewModel
{
    public ClientStatusEnum? Status { get; set; }

    /// <summary>
    /// Designe des clients par identifiant pour les tuiles qui ne savent pas exprimer leur
    /// selection autrement (« non demarres » notamment). Bornee a 200 : une liste non bornee
    /// venue du client est un vecteur d'abus. La borne exacte est revalidee cote service
    /// (<c>ClientService</c>), un appel direct ne devant pas pouvoir la contourner.
    /// </summary>
    [MaxLength(200)]
    public List<Guid>? ClientIds { get; set; }

    /// <summary>
    /// Seuil de 80% sur au moins un des quatre plafonds (chorales, membres, stockage, taille
    /// de fichier), calcule directement en base au moment de l'appel — meme regle que
    /// <c>AdminDashboardService.ComputeClientsNearCapAsync</c>, mais evaluee a la
    /// demande plutot que figee a l'instant du chargement du tableau de bord.
    /// </summary>
    public bool? NearCap { get; set; }
}
