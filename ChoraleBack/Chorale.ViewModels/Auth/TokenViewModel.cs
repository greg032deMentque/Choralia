namespace ChoraleBackEnd.ViewModels.Auth;

public sealed class TokenViewModel
{
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public string? DeviceId { get; set; }
}
