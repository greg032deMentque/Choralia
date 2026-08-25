using System.ComponentModel.DataAnnotations;
using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.ViewModels.Clients;

/// <summary>
/// Changement de statut d'une chorale par le <c>ManagerClient</c> de son client (`10-D23`).
/// Ne porte ni <c>Id</c> ni <c>ClientId</c> : les deux viennent uniquement de la route
/// (<c>clientId</c>, <c>choirId</c>), jamais du corps — un <c>Id</c> de corps permettrait de
/// cibler une chorale différente de celle de l'URL.
/// </summary>
public sealed class ChangeClientChoirStatusViewModel
{
    /// <summary>
    /// Nouveau statut. <b>Nullable à dessein</b>, même raison que
    /// <c>ChangeChoirStatusViewModel.Status</c> (administration générale) : sur un type valeur
    /// non nullable, <c>[Required]</c> ne rejette que <c>null</c>, un champ absent du corps
    /// devenant silencieusement <c>0</c> (<see cref="ChoirStatusEnum.Draft"/>).
    /// <c>[EnumDataType]</c> borne la plage reçue avant même d'atteindre le service.
    /// </summary>
    [Required(ErrorMessage = "Le statut est requis.")]
    [EnumDataType(typeof(ChoirStatusEnum), ErrorMessage = "Statut de chorale inconnu.")]
    public ChoirStatusEnum? Status { get; set; }
}
