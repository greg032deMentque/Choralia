using System.ComponentModel.DataAnnotations;

namespace ChoraleBackEnd.ViewModels.AdminChoirs;

/// <summary>
/// L'administration generale n'ecrit que sur les informations d'une chorale — jamais sur son
/// contenu (`10-D23`, decision produit). Pas de <c>ClientId</c> ici : l'Admin ne deplace pas
/// une chorale d'un client a l'autre.
/// </summary>
public sealed class AdminChoirUpdateViewModel
{
    [Required]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }
}
