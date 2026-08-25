using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.Data.Entities;

public sealed class Event : IAuditable
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public EventTypeEnum Type { get; set; }

    /// <summary>
    /// Lieu de l'evenement. Obligatoire en spec (`04` § Event) — un evenement sans lieu
    /// n'est pas actionnable pour un participant.
    /// </summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>
    /// Statut <b>decide</b> par un responsable. L'etat vu par l'utilisateur se calcule avec
    /// les dates — voir <c>EventStateHelper.EffectiveStatus</c>. `Finished` n'est
    /// deliberement pas stockable.
    /// </summary>
    public EventStatusEnum Status { get; set; }
    public Guid? ChoirId { get; set; }
    public DateTime? ClosedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedByUserId { get; set; }

    public Choir? Choir { get; set; }
    public Space Space { get; set; } = null!;
    public ICollection<SongList> SongLists { get; set; } = [];
}
