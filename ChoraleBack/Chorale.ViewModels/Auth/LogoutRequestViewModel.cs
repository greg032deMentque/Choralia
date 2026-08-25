namespace ChoraleBackEnd.ViewModels.Auth;

public sealed class LogoutRequestViewModel
{
    public string? RefreshToken { get; set; }
    public string? DeviceId { get; set; }
}
