namespace ChoraleBackEnd.Common.Enums;

// ATTENTION : les ordinaux sont PERSISTES en base (stockage entier) et exposes
// au front avec les memes valeurs. Ne jamais reorder ni inserer au milieu :
// toute evolution se fait en FIN de liste, sinon migration de donnees obligatoire.

/// <summary>
/// Cycle de vie d'une chorale (`04` § Chorale, migration 13). Aligne volontairement sur
/// <see cref="EventStatusEnum"/> (memes ordinaux, meme forme) pour que les deux types
/// d'espace se comportent pareil — mais reste un enum DISTINCT : les ordinaux sont
/// persistes et verrouilles independamment par <c>EnumOrdinalsTests</c>, et fusionner les
/// deux ferait qu'une evolution du cycle de vie d'un evenement modifierait silencieusement
/// celui des chorales.
/// </summary>
/// <remarks>
/// Avant ce statut, seul <c>IsDeleted</c> existait sur <c>Choir</c> : l'archivage
/// confondait donc « archivee » (fermee, reversible, content conserve) et « supprimee ».
/// <c>IsDeleted</c> retrouve ici son seul role, la suppression ; <see cref="Status"/> porte
/// desormais la decision humaine sur le cycle de vie.
///
/// Contrairement a un evenement, une chorale n'a pas de date de fin : il n'existe donc pas
/// d'etat effectif calcule (pas de <c>Finished</c>) — voir <c>ChoirStateHelper</c>.
/// </remarks>
public enum ChoirStatusEnum
{
    /// <summary>
    /// En cours de creation (pupitres a definir, membres pas encore invites). Visible du
    /// seul createur et des <c>Manager</c> de la chorale. Invisible des membres simples.
    /// </summary>
    Draft = 0,

    /// <summary>En activite. Fonctionnement normal, visible de tous ses membres actifs.</summary>
    Published = 1,

    /// <summary>
    /// Activity interrompue sans fermeture definitive. Reste visible des membres, mais son
    /// contenu passe en lecture seule : plus aucune ecriture n'y est autorisee. Reversible
    /// vers <see cref="Publie"/>.
    /// </summary>
    Cancelled = 2,

    /// <summary>
    /// Fermee. Contenu conserve mais invisible des membres. Reversible vers
    /// <see cref="Publie"/> (reactivation, decision produit `10-Q22`).
    /// </summary>
    Archived = 3
}
