using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.Data.Entities;

public sealed class Choir : IAuditable
{
    public Guid Id { get; set; }

    /// <summary>
    /// Client de rattachement. Obligatoire : il n'existe pas de chorale sans client
    /// (`04` § Client, `10-D23`). C'est ce qui permet de suspendre l'acces a toutes les
    /// chorales d'un client d'un seul geste.
    /// </summary>
    public Guid ClientId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// Cycle de vie (migration 13). Porte desormais seule la decision archive/actif —
    /// <see cref="IsDeleted"/> ne sert plus que la suppression, jamais l'archivage.
    /// </summary>
    public ChoirStatusEnum Status { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedByUserId { get; set; }

    public string? ImageUrl { get; set; }

    public Client Client { get; set; } = null!;
    public Space Space { get; set; } = null!;
    public ICollection<Section> Sections { get; set; } = [];
    public ICollection<SpaceMember> Members { get; set; } = [];
    public ICollection<SongList> SongLists { get; set; } = [];
    public ICollection<Event> Events { get; set; } = [];
}
