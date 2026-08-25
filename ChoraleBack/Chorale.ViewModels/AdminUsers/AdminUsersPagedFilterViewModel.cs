namespace ChoraleBackEnd.ViewModels.AdminUsers;

public sealed class AdminUsersPagedFilterViewModel : PaginateViewModel
{
    public bool? IsActive { get; set; }
    public bool? IsGuestAccount { get; set; }
}
