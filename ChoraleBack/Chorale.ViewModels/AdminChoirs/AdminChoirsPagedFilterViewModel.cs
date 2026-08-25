using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.ViewModels.AdminChoirs;

public sealed class AdminChoirsPagedFilterViewModel : PaginateViewModel
{
    public Guid? ClientId { get; set; }

    /// <summary>Remplace l'ancien filtre booléen <c>IsArchivee</c> (migration 13).</summary>
    public ChoirStatusEnum? Status { get; set; }

    public bool? InactiveFor30Days { get; set; }
}
