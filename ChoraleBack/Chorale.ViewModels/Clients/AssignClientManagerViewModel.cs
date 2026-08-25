using System.ComponentModel.DataAnnotations;

namespace ChoraleBackEnd.ViewModels.Clients;

/// <summary>
/// Designation d'un responsable cote client. L'utilisateur doit deja avoir un compte :
/// ce n'est pas un flux d'invitation.
/// </summary>
public sealed class AssignClientManagerViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;
}
