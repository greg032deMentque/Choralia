using System.ComponentModel.DataAnnotations;
using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.ViewModels.Instructions;

public sealed class CreateInstructionViewModel
{
    /// <summary>Chant porteur de la consigne. Sa chorale devient celle de la consigne.</summary>
    [Required]
    public Guid SongId { get; set; }

    /// <summary>
    /// Nul = consigne adressee a tout le choeur sur ce chant (responsable uniquement).
    /// Renseigne = consigne de pupitre sur ce chant, seul cas ouvert au chef de pupitre, et
    /// uniquement sur SA voix.
    /// </summary>
    public VoicePartEnum? VoicePart { get; set; }

    [MaxLength(200)]
    public string? Title { get; set; }

    [Required]
    public string Content { get; set; } = string.Empty;
}
