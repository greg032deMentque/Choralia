namespace ChoraleBackEnd.ViewModels.Onboarding;

/// <summary>
/// Etat du code de rattachement d'un espace, cote Responsable. <see cref="Code"/> et
/// <see cref="ExpiresAt"/> sont nuls quand aucun code n'a jamais ete genere pour cet espace.
/// </summary>
public sealed class JoinCodeViewModel
{
    public string? Code { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; }
}
