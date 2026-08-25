using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.Data.Entities;

public sealed class Space : IAuditable
{
    public Guid Id { get; set; }
    public SpaceTypeEnum SpaceType { get; set; }

    /// <summary>
    /// Client de rattachement, chemin unique pour resolve le client d'un espace, chorale
    /// ou evenement confondus (`10-D23`). Pour une chorale, doit rester egal a
    /// <see cref="Choir.ClientId"/> — voir <c>ChoirService</c> pour la resynchronisation.
    /// </summary>
    public Guid ClientId { get; set; }

    public DateTime? EndDate { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedByUserId { get; set; }

    public Client Client { get; set; } = null!;
}
