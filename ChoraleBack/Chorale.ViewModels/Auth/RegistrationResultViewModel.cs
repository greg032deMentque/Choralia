namespace ChoraleBackEnd.ViewModels.Auth;

/// <summary>
/// Reponse UNIQUE de <c>POST /api/auth/Register</c>, quel que soit le cas reel (email libre,
/// compte deja complet, compte invite non revendique) — decision produit : la
/// desambiguisation se fait dans l'email envoye, jamais dans la reponse HTTP.
/// </summary>
public sealed class RegistrationResultViewModel
{
    public string Message { get; set; } = string.Empty;
}
