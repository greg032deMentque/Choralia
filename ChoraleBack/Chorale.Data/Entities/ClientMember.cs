using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.Data.Entities;

/// <summary>
/// Appartenance d'un utilisateur a un <see cref="Client"/>, avec son role.
/// </summary>
/// <remarks>
/// Deliberement distincte de <see cref="SpaceMember"/> : un client n'est pas un espace
/// (`10-D23` ecarte explicitement cette option). Reutiliser l'appartenance a un espace
/// aurait fait heriter le client de la presence, du RSVP et de la date de fin, qui n'ont
/// aucun sens pour lui.
///
/// Porte un <see cref="Role"/> plutot que de s'appeler « ResponsableClient » : nommer une
/// table d'apres son unique valeur de role obligerait a la renommer des qu'un second role
/// client apparait.
/// </remarks>
public sealed class ClientMember : IAuditable
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public UserRoleEnum Role { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedByUserId { get; set; }

    public Client Client { get; set; } = null!;
    public User User { get; set; } = null!;
}
