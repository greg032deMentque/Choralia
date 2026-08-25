using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.Data.Entities;

/// <summary>
/// Demande d'adhesion a un espace, deposee via un code de rattachement (lot 6). A la
/// difference du lien d'invitation nominatif — qui rattache directement — le canal decide
/// seulement si l'admission est deja prise : c'est le Responsable de l'espace qui l'admet ou
/// la refuse.
/// </summary>
public sealed class MembershipRequest : IAuditable
{
    public Guid Id { get; set; }
    public Guid SpaceId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public MembershipRequestStatusEnum Status { get; set; }

    /// <summary>Mot facultatif du demandeur, visible du Responsable.</summary>
    public string? Message { get; set; }

    /// <summary>Motif de refus interne : jamais communique au demandeur (decision produit).</summary>
    public string? DeclineReason { get; set; }

    public string? HandledByUserId { get; set; }
    public DateTime? HandledAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedByUserId { get; set; }

    public Space Space { get; set; } = null!;
    public User User { get; set; } = null!;
}
