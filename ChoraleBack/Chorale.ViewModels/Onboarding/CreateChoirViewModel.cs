using System.ComponentModel.DataAnnotations;

namespace ChoraleBackEnd.ViewModels.Onboarding;

public sealed class CreateChoirViewModel
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>
    /// Facultatif : paroisse, ecole de musique, association. Vide, un Client est cree en
    /// silence, nomme d'apres la chorale — le mot "Client" n'apparait jamais cote utilisateur.
    /// </summary>
    [MaxLength(150)]
    public string? Structure { get; set; }
}
