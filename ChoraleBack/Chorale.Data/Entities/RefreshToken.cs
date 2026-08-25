namespace ChoraleBackEnd.Data.Entities;

public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string? DeviceId { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public DateTime CreatedUtc { get; set; }
    public bool IsRevoked { get; set; }
    public User User { get; set; } = null!;
}
