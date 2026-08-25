namespace ChoraleBackEnd.ViewModels.Auth;

public sealed class AuthenticatedUserViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Firstname { get; set; } = string.Empty;
    public string Lastname { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];

    public List<SpaceRoleAssignmentViewModel> SpaceRoles { get; set; } = [];
    public List<ClientRoleAssignmentViewModel> ClientRoles { get; set; } = [];
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
}
