using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.Data.Entities;

/// <summary>
/// Structure qui souscrit au service. Regroupe une ou plusieurs chorales (`10-D23`).
/// </summary>
/// <remarks>
/// Porte trois responsabilites, et trois seulement : identite et activation, autonomie du
/// client via le role <see cref="UserRoleEnum.ClientManager"/>, et limites de service.
/// La facturation n'est pas scope ici — le client sert de point de rattachement a un futur
/// abonnement, dont le calendrier reste ouvert (`10-D31`).
/// </remarks>
public sealed class Client : IAuditable
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public ClientStatusEnum Status { get; set; }

    /// <summary>
    /// Count maximal de chorales. `D21` posant un produit unique au meme prix, les quatre
    /// limites portent des valeurs par defaut uniformes, surchargeables a la marge par
    /// l'administration generale seule.
    /// </summary>
    public int ChoirLimit { get; set; }

    /// <summary>Count maximal de membres, agrege sur toutes les chorales du client.</summary>
    public int MemberLimit { get; set; }

    /// <summary>Volume total de files autorise, partitions et enregistrements confondus.</summary>
    public long StorageQuotaBytes { get; set; }

    /// <summary>Taille maximale d'un fichier unitaire.</summary>
    public long MaxFileSizeBytes { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedByUserId { get; set; }

    public ICollection<Choir> Choirs { get; set; } = [];
    public ICollection<ClientMember> Members { get; set; } = [];

    /// <summary>
    /// Valeurs par defaut appliquees a la creation. Regroupees ici pour qu'il n'existe
    /// qu'un seul endroit a update si l'offre change.
    /// </summary>
    public static class DefaultLimits
    {
        public const int Choirs = 5;
        public const int Members = 250;
        public const long StorageOctets = 5L * 1024 * 1024 * 1024;
        public const long FileSizeBytes = 100L * 1024 * 1024;
    }

    /// <summary>
    /// Client technique cree par la migration <c>AjouteClientSurSpace</c> pour rattacher les
    /// events autonomes preexistants dont aucun client n'etait derivable (`10-D23`). GUID
    /// fixe : doit rester identique a celui poste en dur dans cette migration.
    /// </summary>
    public static class ClientTechnique
    {
        public const string WithoutStructureId = "11111111-1111-1111-1111-111111111111";

        /// <summary>
        /// Meme valeur que <see cref="WithoutStructureId"/>, deja parsee. Expose ici parce
        /// que trois appelants la comparaient a un <c>ClientId</c> et refaisaient chacun leur
        /// <c>Guid.Parse</c> dans un champ prive : trois copies d'une meme constante que rien
        /// ne tenait synchronisees.
        /// </summary>
        public static readonly Guid WithoutStructure = Guid.Parse(WithoutStructureId);
    }
}
