namespace ChoraleBackEnd.Data.Entities;

/// <summary>
/// Code de rattachement d'un espace (lot 6, decision produit `10-Q22` inscription
/// auto-service) : le canal qui transforme une simple demande en adhesion a valider, par
/// opposition au lien d'invitation nominatif qui rattache directement.
/// </summary>
/// <remarks>
/// Un seul code actif par espace a la fois (index unique filtre sur <see cref="IsActive"/>) :
/// generer ou faire tourner desactive immediatement l'ancien. Le code lui-meme est le secret
/// partage — il n'est pas hache comme un mot de passe, une recherche directe par valeur doit
/// rester possible pour <c>PreviewCode</c> et <c>RequestMembership</c>.
/// </remarks>
public sealed class SpaceJoinCode : IAuditable
{
    public Guid Id { get; set; }
    public Guid SpaceId { get; set; }

    /// <summary>Format <c>XXXX-XXXX</c>, alphabet sans caractere ambigu (ni 0/O, ni 1/I/L).</summary>
    public string Code { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
    public bool IsActive { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedByUserId { get; set; }

    public Space Space { get; set; } = null!;
}
