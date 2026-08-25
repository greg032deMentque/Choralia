namespace ChoraleBackEnd.Common.Enums;

// ATTENTION : les ordinaux sont PERSISTES en base (stockage entier) et exposes
// au front avec les memes valeurs. Ne jamais reorder ni inserer au milieu :
// toute evolution se fait en FIN de liste, sinon migration de donnees obligatoire.

/// <summary>
/// Cycle de vie d'un client (`04` § Client, decision `10-D23`).
/// </summary>
public enum ClientStatusEnum
{
    /// <summary>Acces ouvert pour toutes ses chorales.</summary>
    Active = 0,

    /// <summary>
    /// Acces refuse pour toutes ses chorales, d'un seul geste. Reversible vers Active —
    /// c'est la raison d'etre du palier.
    /// </summary>
    Suspended = 1,

    /// <summary>Acces refuse, conserve en historique. Terminal en V1.</summary>
    Archived = 2
}
