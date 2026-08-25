using Microsoft.AspNetCore.Identity;

namespace ChoraleBackEnd.Data.Entities;

public sealed class User : IdentityUser, IAuditable
{
    public string Firstname { get; set; } = string.Empty;
    public string Lastname { get; set; } = string.Empty;
    public DateTime? LastConnection { get; set; }
    public DateTime? LastActive { get; set; }
    public bool IsActive { get; set; }
    public bool IsGuestAccount { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedByUserId { get; set; }
}
