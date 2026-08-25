using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.Common.Helpers;

/// <summary>
/// Transitions autorisees du statut d'une chorale (migration 13). Miroir de
/// <see cref="EventStateHelper.IsTransitionAllowed"/> : sans cette table, n'importe quel
/// statut serait atteignable depuis n'importe quel autre.
/// </summary>
/// <remarks>
/// Contrairement a un evenement, une chorale n'a pas de date de fin : il n'existe donc pas
/// d'etat effectif calcule (pas de <c>Finished</c>), et pas de <c>EffectiveState</c> pour cette
/// entite.
/// </remarks>
public static class ChoirStateHelper
{
    public static bool IsTransitionAllowed(ChoirStatusEnum depuis, ChoirStatusEnum vers)
        => (depuis, vers) switch
        {
            (ChoirStatusEnum.Draft, ChoirStatusEnum.Published) => true,
            (ChoirStatusEnum.Draft, ChoirStatusEnum.Archived) => true,
            (ChoirStatusEnum.Published, ChoirStatusEnum.Cancelled) => true,
            (ChoirStatusEnum.Published, ChoirStatusEnum.Archived) => true,
            (ChoirStatusEnum.Cancelled, ChoirStatusEnum.Published) => true,
            (ChoirStatusEnum.Cancelled, ChoirStatusEnum.Archived) => true,
            // Reactivation (decision utilisateur `10-Q22`) : seule transition qui revient en
            // arriere, ouverte uniquement depuis Archive.
            (ChoirStatusEnum.Archived, ChoirStatusEnum.Published) => true,
            _ => false
        };
}
