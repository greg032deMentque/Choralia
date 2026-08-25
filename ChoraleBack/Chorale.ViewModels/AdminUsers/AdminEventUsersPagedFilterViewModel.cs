using System.ComponentModel.DataAnnotations;
using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.ViewModels.AdminUsers;

public sealed class AdminEventUsersPagedFilterViewModel : PaginateViewModel
{
    /// <summary>
    /// Designe des evenements par identifiant pour la modale de selection multiple de
    /// /admin/users. Bornee a 200, meme regle que <c>ClientsPagedFilterViewModel.ClientIds</c> :
    /// la borne exacte est revalidee cote service (<c>AdminUserQueryService</c>).
    /// </summary>
    [MaxLength(200)]
    public List<Guid>? EventIds { get; set; }

    public UserRoleEnum? Role { get; set; }
    public AttendanceEnum? Presence { get; set; }
    public bool? Upcoming { get; set; }
}
