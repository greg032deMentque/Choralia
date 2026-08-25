namespace ChoraleBackEnd.ViewModels.Auth;

public sealed class ClientRoleAssignmentViewModel
{
    public Guid ClientId { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];
}
