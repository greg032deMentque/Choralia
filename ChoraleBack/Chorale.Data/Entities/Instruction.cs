using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.Data.Entities;

/// <summary>
/// Consigne attachee a un chant (`04` § Instructions et documents).
/// </summary>
/// <remarks>
/// Cible unique depuis la migration `InstructionsSongScopeOnly` : les portees chorale,
/// pupitre et evenement ont ete supprimees du modele (decision produit,
/// `Spec/chorale/10-decisions.md`), avec les colonnes `Scope`, `ChoirId` et `EventId` et la
/// contrainte `CK_Instruction_Scope` qui garantissait leur exclusivite. La chorale d'une
/// consigne n'est plus une colonne : elle se deduit de `Song.ChoirId`, seule source possible.
/// </remarks>
public sealed class Instruction : IAuditable
{
    public Guid Id { get; set; }

    /// <summary>Chant porteur de la consigne. Sa chorale est la chorale de la consigne.</summary>
    public Guid SongId { get; set; }

    /// <summary>
    /// Restreint la consigne a un pupitre du chant. Nul = consigne adressee a tout le choeur
    /// sur ce chant, reservee au responsable ; renseigne = seul cas ouvert au chef de pupitre,
    /// et uniquement sur SA voix (voir InstructionService.EnsureWriteAsync).
    /// </summary>
    public VoicePartEnum? VoicePart { get; set; }

    public string? Title { get; set; }
    public string Content { get; set; } = string.Empty;
    public InstructionStatusEnum Status { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string AuthorUserId { get; set; } = string.Empty;

    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedByUserId { get; set; }

    public Song Song { get; set; } = null!;
    public User Author { get; set; } = null!;
}
