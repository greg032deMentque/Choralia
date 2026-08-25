using System.ComponentModel.DataAnnotations;
using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.ViewModels.Clients;

public sealed class ChangeClientStatusViewModel
{
    [Required]
    public Guid Id { get; set; }

    /// <summary>
    /// Nouveau statut. <b>Nullable a dessein.</b>
    /// </summary>
    /// <remarks>
    /// Sur un type valeur non nullable, <c>[Required]</c> ne rejette que <c>null</c> : un
    /// champ absent du corps de requete devient <c>0</c>, soit <c>Active</c>. Une requete
    /// tronquee levait donc une suspension sans que personne l'ait demande. Rendre le type
    /// nullable est ce qui fait que <c>[Required]</c> protege reellement.
    ///
    /// <c>[EnumDataType]</c> borne la plage : sans lui, <c>Status = 99</c> etait accepte et
    /// persiste, laissant le client dans un etat ni actif, ni suspendu, ni archive — hors
    /// d'atteinte de la regle « archive est terminal ».
    /// </remarks>
    [Required(ErrorMessage = "Le statut est requis.")]
    [EnumDataType(typeof(ClientStatusEnum), ErrorMessage = "Statut de client inconnu.")]
    public ClientStatusEnum? Status { get; set; }
}
