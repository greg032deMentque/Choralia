using System.ComponentModel.DataAnnotations;

namespace ChoraleBackEnd.ViewModels.Clients;

/// <summary>
/// Reprend champ pour champ <see cref="CreateClientViewModel"/> plus <see cref="Id"/>.
/// Volontairement non factorise par heritage : une classe de base commune ferait qu'ajouter
/// un champ modifiable a la creation l'ouvrirait automatiquement a la mise a jour, et
/// inversement — les deux contrats doivent pouvoir diverger sans effet de bord.
/// </summary>
public sealed class UpdateClientViewModel
{
    [Required]
    public Guid Id { get; set; }

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? ContactName { get; set; }

    [EmailAddress, MaxLength(256)]
    public string? ContactEmail { get; set; }
}
