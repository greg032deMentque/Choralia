using System.ComponentModel.DataAnnotations;
using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.ViewModels.ChoirMembers;

public sealed class InviteMemberViewModel
{
    [Required]
    public Guid ChoirId { get; set; }

    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Firstname { get; set; }

    [MaxLength(100)]
    public string? Lastname { get; set; }

    /// <summary>
    /// Voix principale du membre invité. `02` §132 impose qu'une ligne d'appartenance en
    /// porte toujours une, mais le champ reste OPTIONNEL ici : le back est déployé avant le
    /// front, et le rendre requis casserait l'invitation en production pendant cette
    /// fenêtre. Le passage en requis fait l'objet d'un lot ultérieur.
    /// </summary>
    public VoicePartEnum? PrimaryVoicePart { get; set; }
}
