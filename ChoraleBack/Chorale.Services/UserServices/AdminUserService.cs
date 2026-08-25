using System.Linq.Expressions;
using System.Net;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Helpers;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.AdminUsers;
using Microsoft.EntityFrameworkCore;

namespace ChoraleBackEnd.Services.UserServices;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Common.Constants;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.Technical;

public interface IAdminUserService
{
    Task<AdminUserListItemViewModel> CreateAsync(CreateAdminUserViewModel model, CancellationToken ct = default);
    Task<AdminUserDetailViewModel> UpdateIdentityAsync(AdminUserUpdateIdentityViewModel model, CancellationToken ct = default);
    Task<AdminUserDetailViewModel> SetActiveAsync(string userId, bool isActive, CancellationToken ct = default);
    Task ResetPasswordAsync(string userId, CancellationToken ct = default);
    Task ResendInvitationAsync(string userId, CancellationToken ct = default);
    Task DeleteAsync(string userId, CancellationToken ct = default);
}

/// <summary>
/// Cycle de vie d'un compte cote administration generale : creation d'administrateur, identite,
/// activation, reinitialisation de mot de passe, renvoi d'invitation, suppression.
/// </summary>
/// <remarks>
/// Les listings et la fiche detaillee vivent dans <see cref="IAdminUserQueryService"/> : ce
/// service s'appuie dessus pour renvoyer la fiche a jour apres une ecriture, jamais l'inverse.
/// </remarks>
public sealed class AdminUserService : BaseService, IAdminUserService
{
    // Adresse liberee a la suppression : le compte est soft-delete mais son email doit
    // redevenir disponible pour une future inscription, sans jamais entrer en collision.
    private const string ReleasedEmailDomain = "@supprime.chorale.invalid";

    private readonly IAuditLogService _auditLogService;
    private readonly IAccountService _accountService;
    private readonly IEmailService _emailService;
    private readonly IAdminUserQueryService _adminUserQueryService;

    public AdminUserService(
        IServiceProvider serviceProvider,
        IAuditLogService auditLogService,
        IAccountService accountService,
        IEmailService emailService,
        IAdminUserQueryService adminUserQueryService)
        : base(serviceProvider)
    {
        _auditLogService = auditLogService;
        _accountService = accountService;
        _emailService = emailService;
        _adminUserQueryService = adminUserQueryService;
    }

    public async Task<AdminUserListItemViewModel> CreateAsync(CreateAdminUserViewModel model, CancellationToken ct = default)
    {
        var existing = await _userManager.FindByEmailAsync(model.Email);
        if (existing is not null)
            throw new CustomException(HttpStatusCode.Conflict, "Email déjà utilisé.");

        var user = _mapper.Map<User>(model);
        user.Id = ChoraleDbContext.NewIdGuid().ToString();
        user.UserName = model.Email;
        user.EmailConfirmed = true;
        user.IsActive = true;

        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
            throw new CustomException(HttpStatusCode.BadRequest, "Création échouée.",
                result.Errors.Select(e => e.Description).ToList());

        await _userManager.AddToRoleAsync(user, UserRoleEnum.Admin.ToString());

        _auditLogService.Record("AdminUserCreated", nameof(User), user.Id, $"Email={user.Email}");
        await _context.SaveChangesAsync(ct);

        return _mapper.Map<AdminUserListItemViewModel>(user);
    }

    public async Task<AdminUserDetailViewModel> UpdateIdentityAsync(
        AdminUserUpdateIdentityViewModel model, CancellationToken ct = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == model.Id, ct)
            ?? throw new KeyNotFoundException($"User {model.Id} not found.");

        var newEmail = model.Email.Trim();

        if (!string.Equals(user.Email, newEmail, StringComparison.OrdinalIgnoreCase))
        {
            var conflict = await _context.Users.AnyAsync(
                u => u.Id != model.Id && u.Email != null && u.Email == newEmail, ct);

            if (conflict)
                throw new CustomException(HttpStatusCode.Conflict, "Cet email est déjà utilisé par un autre compte.");

            user.Email = newEmail;
            user.NormalizedEmail = newEmail.ToUpperInvariant();
            user.UserName = newEmail;
            user.NormalizedUserName = newEmail.ToUpperInvariant();
        }

        user.Firstname = model.Firstname;
        user.Lastname = model.Lastname;

        _auditLogService.Record("AdminUserIdentityUpdated", nameof(User), user.Id);
        await _context.SaveChangesAsync(ct);

        return await _adminUserQueryService.GetUserDetailAsync(user.Id, ct);
    }

    public async Task<AdminUserDetailViewModel> SetActiveAsync(string userId, bool isActive, CancellationToken ct = default)
    {
        if (userId == _currentUserId && !isActive)
            throw new CustomException(HttpStatusCode.Forbidden, "Vous ne pouvez pas désactiver votre propre compte.");

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new KeyNotFoundException($"User {userId} not found.");

        user.IsActive = isActive;

        _auditLogService.Record(isActive ? "AdminUserActivated" : "AdminUserDeactivated", nameof(User), user.Id);
        await _context.SaveChangesAsync(ct);

        return await _adminUserQueryService.GetUserDetailAsync(user.Id, ct);
    }

    public async Task ResetPasswordAsync(string userId, CancellationToken ct = default)
    {
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new KeyNotFoundException($"User {userId} not found.");

        if (user.IsGuestAccount && !user.EmailConfirmed)
        {
            await SendPasswordSetupLinkAsync(user, isInvitation: true, ct);
            _auditLogService.Record("AdminUserInvitationResentViaReset", nameof(User), user.Id);
            await _context.SaveChangesAsync(ct);
            return;
        }

        await _accountService.ForgotPassword(user.Email ?? string.Empty);
        _auditLogService.Record("AdminUserPasswordResetRequested", nameof(User), user.Id);
        await _context.SaveChangesAsync(ct);
    }

    public async Task ResendInvitationAsync(string userId, CancellationToken ct = default)
    {
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new KeyNotFoundException($"User {userId} not found.");

        if (!user.IsGuestAccount || user.EmailConfirmed)
            throw new CustomException(HttpStatusCode.Conflict, "Ce compte n'est pas une invitation en attente.");

        await SendPasswordSetupLinkAsync(user, isInvitation: true, ct);
        _auditLogService.Record("AdminUserInvitationResent", nameof(User), user.Id);
        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string userId, CancellationToken ct = default)
    {
        if (userId == _currentUserId)
            throw new CustomException(HttpStatusCode.Forbidden, "Vous ne pouvez pas supprimer votre propre compte.");

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new KeyNotFoundException($"User {userId} not found.");

        if (await IsLastAdminAsync(userId, ct))
            throw new CustomException(HttpStatusCode.Conflict, "Impossible de supprimer le dernier administrateur.");

        var assignments = await _context.SpaceMembers
            .Where(m => m.UserId == userId)
            .ToListAsync(ct);

        foreach (var assignment in assignments)
            assignment.IsDeleted = true;

        user.IsDeleted = true;
        user.IsActive = false;

        var releasedEmail = $"supprime-{user.Id}{ReleasedEmailDomain}";
        user.Email = releasedEmail;
        user.NormalizedEmail = releasedEmail.ToUpperInvariant();
        user.UserName = releasedEmail;
        user.NormalizedUserName = releasedEmail.ToUpperInvariant();

        _auditLogService.Record("AdminUserDeleted", nameof(User), user.Id);
        await _context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Émet le lien de définition de mot de passe. Les deux cas ne partagent ni le jeton ni la
    /// route : l'invitation passe par le fournisseur dédié (durée de vie longue, route
    /// d'activation), la réinitialisation garde le jeton Identity par défaut (1 h).
    /// </summary>
    private async Task SendPasswordSetupLinkAsync(User user, bool isInvitation, CancellationToken ct)
    {
        var token = isInvitation
            ? await _userManager.GenerateUserTokenAsync(
                user,
                AccountTokenConstants.InvitationTokenProvider,
                AccountTokenConstants.AccountActivationPurpose)
            : await _userManager.GeneratePasswordResetTokenAsync(user);

        var frontUrl = _configuration["Frontend:BaseUrl"]
            ?? throw new InvalidOperationException("Frontend:BaseUrl manquant.");

        var (route, subject, action) = isInvitation
            ? ("activate-account", "Renvoi de votre invitation", "définir votre mot de passe et activer votre compte")
            : ("reset-password", "Réinitialisation de votre mot de passe", "réinitialiser votre mot de passe");

        var link = $"{frontUrl}/{route}?userId={user.Id}&token={UrlTokenHelper.Encode(token)}";
        var encodedLink = WebUtility.HtmlEncode(link);

        var body = $"<p>Cliquez sur le lien suivant pour {action} : <a href=\"{encodedLink}\">{encodedLink}</a></p>";

        await _emailService.SendAsync(user.Email ?? string.Empty, subject, body, ct);
    }

    private async Task<bool> IsLastAdminAsync(string userId, CancellationToken ct)
    {
        var isAdmin = await (
            from ur in _context.UserRoles
            join r in _context.Roles on ur.RoleId equals r.Id
            where ur.UserId == userId && r.Name == UserRoleEnum.Admin.ToString()
            select ur.UserId
        ).AnyAsync(ct);

        if (!isAdmin) return false;

        var adminCount = await (
            from u in _context.Users
            join ur in _context.UserRoles on u.Id equals ur.UserId
            join r in _context.Roles on ur.RoleId equals r.Id
            where r.Name == UserRoleEnum.Admin.ToString() && !u.IsDeleted
            select u.Id
        ).Distinct().CountAsync(ct);

        return adminCount <= 1;
    }
}
