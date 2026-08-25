namespace ChoraleBackEnd.ViewModels.Guests;

public sealed class PurgeCandidateItemViewModel
{
    public string UserId { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Firstname { get; set; } = string.Empty;
    public string Lastname { get; set; } = string.Empty;
    public DateTime LastActivityAt { get; set; }
}
