namespace ChoraleBackEnd.Common.Enums;

// ATTENTION : les ordinaux sont PERSISTES en base (stockage entier) et exposes
// au front avec les memes valeurs. Ne jamais reorder ni inserer au milieu :
// toute evolution se fait en FIN de liste, sinon migration de donnees obligatoire.

/// <summary>
/// Cycle de vie d'un evenement (`04` § Event).
/// </summary>
/// <remarks>
/// <b>Finished n'est pas un statut stocke.</b> La spec dit qu'un evenement passe
/// automatiquement a `termine` une fois sa date passee : le stocker creerait une seconde
/// source de verite, qui divergerait des dates des qu'une date est modifiee ou qu'aucun
/// traitement de fond ne tourne — et il n'y a pas de worker sur ce projet.
///
/// Le statut stocke porte donc la seule decision humaine, et l'etat effectif se calcule
/// avec les dates : voir <c>EventStateHelper.EffectiveStatus</c>.
/// </remarks>
public enum EventStatusEnum
{
    /// <summary>Invisible des membres. Modifiable entierement.</summary>
    Draft = 0,

    /// <summary>Visible des membres. Devient `Finished` une fois la date passee.</summary>
    Published = 1,

    /// <summary>
    /// Annule par un responsable. Reste <b>visible</b> des membres avec son etat affiche —
    /// il n'est pas supprime (`04` § Event, Regles).
    /// </summary>
    Cancelled = 2,

    /// <summary>Masque par defaut, conserve en historique.</summary>
    Archived = 3
}

/// <summary>
/// Etat effectif d'un evenement, statut stocke et dates combines. C'est ce que voit
/// l'utilisateur ; <see cref="EventStatusEnum"/> est ce que decide un responsable.
/// </summary>
public enum EventEffectiveStateEnum
{
    Draft = 0,
    Published = 1,
    Finished = 2,
    Cancelled = 3,
    Archived = 4
}
