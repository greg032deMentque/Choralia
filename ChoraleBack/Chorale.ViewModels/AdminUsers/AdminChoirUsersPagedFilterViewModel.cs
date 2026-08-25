using System.ComponentModel.DataAnnotations;
using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.ViewModels.AdminUsers;

public sealed class AdminChoirUsersPagedFilterViewModel : PaginateViewModel
{
    /// <summary>
    /// Designe des chorales par identifiant pour la modale de selection multiple de
    /// /admin/users. Bornee a 200, meme regle que <c>ClientsPagedFilterViewModel.ClientIds</c> :
    /// la borne exacte est revalidee cote service (<c>AdminUserQueryService</c>).
    /// </summary>
    [MaxLength(200)]
    public List<Guid>? ChoirIds { get; set; }

    public UserRoleEnum? Role { get; set; }
    public MemberStatusEnum? Status { get; set; }
    public VoicePartEnum? VoicePart { get; set; }
    public bool? IsActive { get; set; }
}
