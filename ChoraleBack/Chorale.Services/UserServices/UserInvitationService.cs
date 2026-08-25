using System.Net;
using System.Security.Cryptography;
using ChoraleBackEnd.Common.Constants;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Helpers;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.Technical;
using Microsoft.EntityFrameworkCore;

namespace ChoraleBackEnd.Services.UserServices;

public interface IUserInvitationService
{
    Task<User> InviteGuestAsync(
        string email,
        string? firstname,
        SpaceTypeEnum spaceType,
        string spaceName,
        string? lastname = null,
        CancellationToken ct = default);
}

public sealed class UserInvitationService : BaseService, IUserInvitationService
{
    private const int TemporaryPasswordLength = 16;
    private const string TemporaryPasswordSpecialChars = "!@#$%^&*";

    private readonly IEmailService _emailService;

    public UserInvitationService(IServiceProvider serviceProvider, IEmailService emailService)
        : base(serviceProvider)
    {
        _emailService = emailService;
    }

    public async Task<User> InviteGuestAsync(
        string email,
        string? firstname,
        SpaceTypeEnum spaceType,
        string spaceName,
        string? lastname = null,
        CancellationToken ct = default)
    {
        var trimmedEmail = email.Trim();

        // Collision d'email jugée sur NormalizedEmail, comme UserManager.FindByEmailAsync :
        // la comparaison brute laissait « Alex@test.com » créer un second compte à côté de
        // « alex@test.com », alors que les autres chemins d'invitation les voyaient égaux.
        // IgnoreQueryFilters est indispensable ici : un compte soft-deleté doit être vu, sinon
        // il est recréé en doublon et l'index unique d'email rejette l'invitation.
        var normalizedEmail = _userManager.NormalizeEmail(trimmedEmail);

        var existing = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, ct);

        if (existing is null)
            return await CreateGuestAsync(trimmedEmail, firstname, lastname, spaceType, spaceName, ct);

        if (existing.IsDeleted)
        {
            if (!existing.IsGuestAccount)
                throw new CustomException(
                    "Tentative de réactivation d'un compte désactivé non-invité.",
                    "Ce compte existe mais n'est plus actif.",
                    HttpStatusCode.Conflict);

            existing.IsDeleted = false;
            await _context.SaveChangesAsync(ct);
            return existing;
        }

        if (existing.IsGuestAccount && !existing.EmailConfirmed)
        {
            // Compte invite jamais revendique : l'email est le SEUL canal de rattachement.
            // Le retourner sans rien envoyer (comportement precedent) laissait l'invite sans
            // aucun moyen de recevoir son lien, en succes silencieux pour l'invitant.
            await SendInvitationAsync(existing, spaceType, spaceName, ct);
            return existing;
        }

        // Compte deja actif et revendique : ni email en double, ni retour silencieux — celui
        // qui invite doit savoir que cette personne a deja un compte utilisable.
        throw new CustomException(
            "Tentative d'invitation d'un compte deja actif et revendique.",
            "Ce compte existe déjà et est actif.",
            HttpStatusCode.Conflict);
    }

    private async Task<User> CreateGuestAsync(
        string email,
        string? firstname,
        string? lastname,
        SpaceTypeEnum spaceType,
        string spaceName,
        CancellationToken ct)
    {
        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            UserName = email,
            Email = email,
            Firstname = firstname ?? string.Empty,
            Lastname = lastname ?? string.Empty,
            IsActive = true,
            IsGuestAccount = true,
            EmailConfirmed = false
        };

        var temporaryPassword = GenerateTemporaryPassword();
        var createResult = await _userManager.CreateAsync(user, temporaryPassword);
        if (!createResult.Succeeded)
        {
            var errors = createResult.Errors.Select(e => e.Description).ToList();
            throw new CustomException(HttpStatusCode.BadRequest, "Impossible de créer le compte invité.", errors);
        }

        await _userManager.AddToRoleAsync(user, UserRoleEnum.Singer.ToString());
        await SendInvitationAsync(user, spaceType, spaceName, ct);

        return user;
    }

    /// <summary>
    /// Objet et corps de l'email selon le type d'espace concerné. Ce service est appelé pour
    /// des invitations de chorale comme d'evenement : un objet fige a demeure faisait croire
    /// a un invite de chorale qu'il rejoignait un evenement.
    /// </summary>
    private async Task SendInvitationAsync(
        User user, SpaceTypeEnum spaceType, string spaceName, CancellationToken ct)
    {
        // Fournisseur dédié, pas GeneratePasswordResetTokenAsync : ce dernier vit 1 h (durée
        // du « mot de passe oublié »), ce qui condamnait tout invité ouvrant son mail le
        // lendemain.
        var token = await _userManager.GenerateUserTokenAsync(
            user,
            AccountTokenConstants.InvitationTokenProvider,
            AccountTokenConstants.AccountActivationPurpose);

        var frontUrl = _configuration["Frontend:BaseUrl"]
            ?? throw new InvalidOperationException("Frontend:BaseUrl manquant.");
        var activationLink = $"{frontUrl}/activate-account?userId={user.Id}&token={UrlTokenHelper.Encode(token)}";

        var (subject, verb) = spaceType == SpaceTypeEnum.Choir
            ? ("Invitation à rejoindre une chorale", "rejoindre la chorale")
            : ("Invitation à rejoindre un événement", "participer à l'événement");

        // Le nom de l'espace est saisi par un utilisateur : interpolé brut, un nom contenant
        // du balisage partait tel quel dans le corps HTML reçu par tous les invités.
        var encodedSpaceName = WebUtility.HtmlEncode(spaceName);
        var encodedLink = WebUtility.HtmlEncode(activationLink);

        await _emailService.SendAsync(
            user.Email!,
            subject,
            $"<p>Vous avez été invité(e) à {verb} « {encodedSpaceName} ». Cliquez sur le lien suivant pour "
            + $"définir votre mot de passe et activer votre compte : <a href=\"{encodedLink}\">{encodedLink}</a></p>",
            ct);
    }

    private static string GenerateTemporaryPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";

        var chars = new List<char>
        {
            upper[RandomNumberGenerator.GetInt32(upper.Length)],
            lower[RandomNumberGenerator.GetInt32(lower.Length)],
            digits[RandomNumberGenerator.GetInt32(digits.Length)],
            TemporaryPasswordSpecialChars[RandomNumberGenerator.GetInt32(TemporaryPasswordSpecialChars.Length)]
        };

        const string all = upper + lower + digits + TemporaryPasswordSpecialChars;
        while (chars.Count < TemporaryPasswordLength)
            chars.Add(all[RandomNumberGenerator.GetInt32(all.Length)]);

        return new string(chars.OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue)).ToArray());
    }
}
