using System.ComponentModel.DataAnnotations;
using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.ViewModels.AdminChoirs;

public sealed class ChangeChoirStatusViewModel
{
    [Required]
    public Guid Id { get; set; }

    /// <summary>
    /// Nouveau statut. <b>Nullable a dessein</b>, meme raison que
    /// <c>ChangeClientStatusViewModel.Status</c> : sur un type valeur non nullable,
    /// <c>[Required]</c> ne rejette que <c>null</c>, un champ absent du corps devenant
    /// silencieusement <c>0</c> (<see cref="ChoirStatusEnum.Draft"/>).
    /// <c>[EnumDataType]</c> borne la plage recue avant meme d'atteindre le service.
    /// </summary>
    [Required(ErrorMessage = "Le statut est requis.")]
    [EnumDataType(typeof(ChoirStatusEnum), ErrorMessage = "Statut de chorale inconnu.")]
    public ChoirStatusEnum? Status { get; set; }
}
