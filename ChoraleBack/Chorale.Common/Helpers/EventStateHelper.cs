using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.Common.Helpers;

public static class EventStateHelper
{
    public static bool IsFinished(DateTime dateDebut, DateTime? dateFin)
        => DateTime.UtcNow > (dateFin ?? dateDebut);

    /// <summary>
    /// Etat effectif d'un evenement : le statut stocke, sauf qu'un evenement `Publie` dont
    /// la date est passee est `Finished`.
    /// </summary>
    /// <remarks>
    /// Calcule plutot que stocke, faute de traitement de fond sur ce projet — un statut
    /// `Finished` en base ne serait jamais mis a jour et divergerait des dates.
    ///
    /// `Annule` et `Archive` ne basculent jamais : une decision humaine ne s'efface pas
    /// parce qu'une date passe. Un evenement annule reste visible avec son etat affiche
    /// (`04` § Event, Regles).
    /// </remarks>
    public static EventEffectiveStateEnum EffectiveStatus(
        EventStatusEnum status, DateTime dateDebut, DateTime? dateFin)
        => status switch
        {
            EventStatusEnum.Draft => EventEffectiveStateEnum.Draft,
            EventStatusEnum.Cancelled => EventEffectiveStateEnum.Cancelled,
            EventStatusEnum.Archived => EventEffectiveStateEnum.Archived,
            EventStatusEnum.Published => IsFinished(dateDebut, dateFin)
                ? EventEffectiveStateEnum.Finished
                : EventEffectiveStateEnum.Published,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Status inconnu.")
        };

    /// <summary>
    /// Transitions autorisees du statut stocke. Toute autre est refusee — sans cette table,
    /// n'importe quel statut serait atteignable depuis n'importe quel autre.
    /// </summary>
    public static bool IsTransitionAllowed(EventStatusEnum depuis, EventStatusEnum vers)
        => (depuis, vers) switch
        {
            (EventStatusEnum.Draft, EventStatusEnum.Published) => true,
            (EventStatusEnum.Draft, EventStatusEnum.Archived) => true,
            (EventStatusEnum.Published, EventStatusEnum.Cancelled) => true,
            (EventStatusEnum.Published, EventStatusEnum.Archived) => true,
            (EventStatusEnum.Cancelled, EventStatusEnum.Archived) => true,
            _ => false
        };

    /// <summary>
    /// Rattachement fige (decision produit) : <c>ChoirId</c> se decide exclusivement a la
    /// creation d'un evenement et ne bouge plus jamais ensuite — un evenement autonome cree
    /// sans chorale le reste, et un evenement de chorale ne peut pas changer de chorale
    /// porteuse. Il n'existe aucun chemin de migration entre les deux.
    /// </summary>
    /// <remarks>
    /// Un changement n'est demande que si <paramref name="requestedChoirId"/> est renseigne
    /// ET different de la valeur actuelle : un corps de requete qui omet <c>ChoirId</c>, ou
    /// qui renvoie la valeur deja en place, n'est jamais une tentative de changement.
    /// </remarks>
    public static bool IsChoirIdChangeRequested(Guid? currentChoirId, Guid? requestedChoirId)
        => requestedChoirId.HasValue && requestedChoirId != currentChoirId;
}
