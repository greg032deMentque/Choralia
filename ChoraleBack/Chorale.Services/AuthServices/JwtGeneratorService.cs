using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SecurityException = System.Security.SecurityException;

namespace ChoraleBackEnd.Services.AuthServices;

public interface IJwtGeneratorService
{
    Task<string> GenerateJwtToken(User user);
    Task<RefreshToken> GenerateUserRefreshToken(User user, string? deviceId);
    Task<(string AccessToken, RefreshToken RefreshToken)> RotateRefresh(string refreshToken, string? deviceId);
    Task RevokeRefreshAsync(string refreshToken);
}

public sealed class JwtGeneratorService : BaseService, IJwtGeneratorService
{
    public JwtGeneratorService(IServiceProvider serviceProvider)
        : base(serviceProvider) { }

    public async Task<string> GenerateJwtToken(User user)
    {
        var secret = _configuration["JWTToken:Secret"]
            ?? throw new InvalidOperationException("JWT secret manquant.");
        var issuer = _configuration["JWTToken:Issuer"] ?? "";
        var audience = _configuration["JWTToken:Audience"] ?? "";
        var expires = int.Parse(_configuration["JWTToken:ExpiresInMinutes"] ?? "60");

        var roles = await _userManager.GetRolesAsync(user);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(ClaimTypes.NameIdentifier, user.Id),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha512);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expires),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task<RefreshToken> GenerateUserRefreshToken(User user, string? deviceId)
    {
        var token = new RefreshToken
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = user.Id,
            Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
            DeviceId = deviceId,
            ExpiresUtc = DateTime.UtcNow.AddDays(30),
            CreatedUtc = DateTime.UtcNow,
            IsRevoked = false
        };
        _context.RefreshTokens.Add(token);
        await _context.SaveChangesAsync();
        return token;
    }

    public async Task<(string AccessToken, RefreshToken RefreshToken)> RotateRefresh(
        string refreshToken, string? deviceId)
    {
        var existing = await _context.RefreshTokens
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Token == refreshToken)
            ?? throw new SecurityException("Refresh token invalide.");

        if (existing.IsRevoked || existing.ExpiresUtc < DateTime.UtcNow)
            throw new SecurityException("Refresh token expiré ou révoqué.");

        // Etat du compte revalide a CHAQUE rotation : desactiver un compte
        // (AdminUserService.SetActiveAsync) ne revoque pas ses refresh tokens, donc sans ce
        // controle la session restait renouvelable jusqu'a 30 jours apres la desactivation.
        if (!existing.User.IsActive || existing.User.IsDeleted)
            throw new SecurityException("Compte désactivé ou supprimé.");

        existing.IsRevoked = true;
        var newAccess = await GenerateJwtToken(existing.User);
        var newRefresh = await GenerateUserRefreshToken(existing.User, deviceId);
        return (newAccess, newRefresh);
    }

    public async Task RevokeRefreshAsync(string refreshToken)
    {
        var token = await _context.RefreshTokens
            .FirstOrDefaultAsync(r => r.Token == refreshToken);
        if (token is null) return;
        token.IsRevoked = true;
        await _context.SaveChangesAsync();
    }
}
