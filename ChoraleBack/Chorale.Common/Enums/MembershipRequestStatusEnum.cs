namespace ChoraleBackEnd.Common.Enums;

// ATTENTION : les ordinaux sont PERSISTES en base (stockage entier) et exposes
// au front avec les memes valeurs. Ne jamais reorder ni inserer au milieu :
// toute evolution se fait en FIN de liste, sinon migration de donnees obligatoire.

/// <summary>
/// Cycle de vie d'une demande d'adhesion a un espace via code de rattachement (lot 6).
/// </summary>
public enum MembershipRequestStatusEnum
{
    /// <summary>Enregistree, en attente de traitement par un Responsable de l'espace.</summary>
    Pending = 0,

    /// <summary>Acceptee : le demandeur est devenu membre de l'espace.</summary>
    Approved = 1,

    /// <summary>Refusee par un Responsable. Bloque une nouvelle demande sur le meme espace pendant 30 jours.</summary>
    Declined = 2,

    /// <summary>Annulee par le demandeur lui-meme avant traitement.</summary>
    Cancelled = 3
}
