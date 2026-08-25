using System.Net;
using System.Text;
using System.Text.Json;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Helpers;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.ViewModels.Auth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace ChoraleBackEnd.Services.AuthServices;

/// <summary>
/// Registration auto-service (lot 6, `10-Q22`). Quatre cas d'entry pour <see cref="RegisterAsync"/>
/// — email libre, compte deja complet (confirme), compte invite non revendique, compte
/// auto-inscrit jamais actif — qui doivent tous produire exactement la meme reponse
/// (decision produit, anti-enumeration) : la desambiguisation se fait dans l'email envoye,
/// jamais dans le corps HTTP.
/// </summary>
public interface IRegistrationService
{
    Task<RegistrationResultViewModel> RegisterAsync(RegisterViewModel model, CancellationToken ct = default);

    /// <summary>
    /// Lien 24h, consommation unique. L'unicite n'est pas scope par un stockage de jetons
    /// consommes : une fois <c>EmailConfirmed</c> passe a vrai, toute nouvelle tentative avec
    /// le meme jeton est refusee — voir la garde en tete de methode.
    /// </summary>
    Task VerifyEmailAsync(string userId, string token, CancellationToken ct = default);

    Task ResendVerificationAsync(string email, CancellationToken ct = default);
}

public sealed class RegistrationService : BaseService, IRegistrationService
{
    private const string DataProtectionPurpose = "Choir.EmailVerification.v1";
    private const string InvariantResponseMessage = "Si votre demande est valide, un email vous a été envoyé.";

    private static readonly TimeSpan DurationValiditeToken = TimeSpan.FromHours(24);

    private readonly IEmailService _emailService;
    private readonly IDataProtector _dataProtector;

    public RegistrationService(
        IServiceProvider serviceProvider,
        IEmailService emailService,
        IDataProtectionProvider dataProtectionProvider)
        : base(serviceProvider)
    {
        _emailService = emailService;
        _dataProtector = dataProtectionProvider.CreateProtector(DataProtectionPurpose);
    }

    public async Task<RegistrationResultViewModel> RegisterAsync(
        RegisterViewModel model, CancellationToken ct = default)
    {
        var email = model.Email.Trim();

        var existing = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        if (existing is null)
        {
            await CreateAccountAndSendActivationAsync(email, model, ct);
        }
        else if (existing.IsGuestAccount && !existing.EmailConfirmed)
        {
            // Meme cout CPU approximatif que la branche nominale (qui hache le mot de passe
            // via CreateAsync) : sans cela, les quatre branches seraient distinguables par leur
            // temps de reponse malgre un corps identique.
            BurnPasswordHashingCost(model.Password);
            await SendClaimEmailAsync(existing, ct);
        }
        else if (!existing.EmailConfirmed)
        {
            // Auto-inscrit (non invite, cf. branche precedente), jamais actif — l'envoi du
            // premier email a pu echouer (SMTP indisponible), ou l'utilisateur a simplement
            // perdu son lien. Sans cette branche, `RegisterAsync` tombait dans la branche
            // "compte existant" et le compte restait bloque a vie : jamais confirme, aucun
            // chemin pour le devenir. Renvoyer un lien d'activation frais est le seul geste
            // qui debloque cette situation (defaut verifie, correction ciblee).
            BurnPasswordHashingCost(model.Password);
            await SendEmailActivationAsync(existing, ct);
        }
        else
        {
            BurnPasswordHashingCost(model.Password);
            await SendExistingAccountEmailAsync(existing, ct);
        }

        return new RegistrationResultViewModel { Message = InvariantResponseMessage };
    }

    public async Task VerifyEmailAsync(string userId, string token, CancellationToken ct = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

        // user.EmailConfirmed deja vrai couvre a la fois "deja verifie" et "jeton reutilise"
        // (meme message unique dans les deux cas, decision produit) : il n'existe pas de
        // stockage separe de jetons consommes, cette garde en tient lieu.
        if (user is null || user.EmailConfirmed || !TryValidateToken(user.Id, token))
            throw new CustomException(HttpStatusCode.BadRequest, "Lien de vérification invalide ou expiré.");

        user.EmailConfirmed = true;
        await _context.SaveChangesAsync(ct);
    }

    public async Task ResendVerificationAsync(string email, CancellationToken ct = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email.Trim(), ct);

        if (user is null || user.EmailConfirmed)
        {
            // Meme silence que ForgotPassword pour un email inconnu : ne pas reveler si le
            // compte existe ou est deja verifie.
            await Task.Delay(Random.Shared.Next(200, 500), ct);
            return;
        }

        await SendEmailActivationAsync(user, ct);
    }

    private async Task CreateAccountAndSendActivationAsync(
        string email, RegisterViewModel model, CancellationToken ct)
    {
        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            UserName = email,
            Email = email,
            Firstname = model.Firstname,
            Lastname = model.Lastname,
            IsActive = true,
            IsGuestAccount = false,
            EmailConfirmed = false
        };

        var createResult = await _userManager.CreateAsync(user, model.Password);
        if (!createResult.Succeeded)
        {
            var errors = createResult.Errors.Select(e => e.Description).ToList();
            throw new CustomException(HttpStatusCode.BadRequest, "Inscription impossible.", errors);
        }

        await _userManager.AddToRoleAsync(user, UserRoleEnum.Singer.ToString());
        await SendEmailActivationAsync(user, ct);
    }

    private async Task SendEmailActivationAsync(User user, CancellationToken ct)
    {
        var token = GenerateToken(user.Id);
        // Doit correspondre EXACTEMENT a RoutePaths.VerifyEmail cote front
        // (ChoralFront/src/app/core/route-paths.ts). Ce lien a deja ete casse deux fois :
        // une premiere par une divergence verify-email / verifier-email, une seconde quand
        // l'anglicisation du front a renomme la route sans que ce littéral suive. Aucun test
        // ne peut le detecter — celui du back epingle une constante, pas la table de routes
        // du front. Toute modification ici ou la-bas doit etre faite des deux cotes.
        var lien = WebUtility.HtmlEncode($"{GetFrontUrl()}/verify-email?userId={user.Id}&token={token}");

        await _emailService.SendAsync(
            user.Email!,
            "Activez votre compte ChoraleHelper",
            $"<p>Cliquez sur le lien suivant pour activer votre compte : <a href=\"{lien}\">{lien}</a></p>",
            ct);
    }

    /// <summary>
    /// Reutilise le jeton de reinitialisation de mot de passe (comme <c>UserInvitationService</c>
    /// pour l'invitation nominative) : suivre ce lien pour la premiere fois definit le mot de
    /// passe et revendique le compte (<c>AccountService.ResetPassword</c> pose deja
    /// <c>EmailConfirmed</c>/<c>IsGuestAccount</c> a la reussite).
    /// </summary>
    private async Task SendClaimEmailAsync(User existing, CancellationToken ct)
    {
        var tokenBase64 = UrlTokenHelper.Encode(
            await _userManager.GeneratePasswordResetTokenAsync(existing));

        var lien = WebUtility.HtmlEncode(
            $"{GetFrontUrl()}/reset-password?userId={existing.Id}&token={tokenBase64}");

        await _emailService.SendAsync(
            existing.Email!,
            "Finalisez votre inscription",
            $"<p>Cliquez sur le lien suivant pour définir votre mot de passe et activer votre compte : "
            + $"<a href=\"{lien}\">{lien}</a></p>",
            ct);
    }

    private async Task SendExistingAccountEmailAsync(User existing, CancellationToken ct)
    {
        var frontUrl = GetFrontUrl();
        var lienConnexion = WebUtility.HtmlEncode($"{frontUrl}/login");
        var forgotPasswordLink = WebUtility.HtmlEncode($"{frontUrl}/forgot-password");

        await _emailService.SendAsync(
            existing.Email!,
            "Vous avez déjà un compte",
            "<p>Un compte existe déjà avec cette adresse email. "
            + $"<a href=\"{lienConnexion}\">Connectez-vous</a> ou "
            + $"<a href=\"{forgotPasswordLink}\">réinitialisez votre mot de passe</a> si vous l'avez oublié.</p>",
            ct);
    }

    private void BurnPasswordHashingCost(string password)
        => _userManager.PasswordHasher.HashPassword(new User(), password);

    // Un repli silencieux ("") produisait un lien relatif inutilisable dans un client mail —
    // un email non delivrable doit echouer bruyamment, pas partir avec un lien mort.
    private string GetFrontUrl()
        => _configuration["Frontend:BaseUrl"]
            ?? throw new InvalidOperationException("Frontend:BaseUrl manquant.");

    private string GenerateToken(string userId)
    {
        var expiresAtUtc = DateTime.UtcNow.Add(DurationValiditeToken);
        var payload = JsonSerializer.Serialize(new TokenPayload(userId, expiresAtUtc));
        return UrlTokenHelper.EncodeBytes(_dataProtector.Protect(Encoding.UTF8.GetBytes(payload)));
    }

    private bool TryValidateToken(string userId, string token)
    {
        try
        {
            var payloadBytes = _dataProtector.Unprotect(UrlTokenHelper.DecodeBytes(token));
            var payload = JsonSerializer.Deserialize<TokenPayload>(payloadBytes);

            return payload is not null
                && payload.UserId == userId
                && payload.ExpiresAtUtc > DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException
            or FormatException or JsonException)
        {
            return false;
        }
    }

    private sealed record TokenPayload(string UserId, DateTime ExpiresAtUtc);
}
