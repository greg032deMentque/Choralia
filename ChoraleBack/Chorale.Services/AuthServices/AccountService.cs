using System.Net;
using ChoraleBackEnd.Common.Constants;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Helpers;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Services.UserServices;
using ChoraleBackEnd.ViewModels.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SecurityException = System.Security.SecurityException;

namespace ChoraleBackEnd.Services.AuthServices;

public interface IAccountService
{
    Task<TokenViewModel?> Login(LoginViewModel model);
    Task<TokenViewModel?> RefreshTokenAsync(TokenViewModel request);
    Task UnlockUser(string userId);
    Task<IdentityResult> ResetPassword(ResetPasswordRequestViewModel model);

    /// <summary>
    /// Revendication d'un compte invité : pose le mot de passe et active le compte à partir
    /// du jeton du lien d'invitation. Consommation unique — poser le mot de passe change le
    /// security stamp, ce qui invalide le jeton.
    /// </summary>
    Task ActivateAccountAsync(ActivateAccountViewModel model, CancellationToken ct = default);

    Task Logout(LogoutRequestViewModel? request);
    Task ForgotPassword(string email);
    Task<AuthenticatedUserViewModel> GetCurrentUserAsync(CancellationToken cancellationToken = default);
}

public sealed class AccountService : BaseService, IAccountService
{
    private const string InvalidActivationLinkMessage = "Lien d'activation invalide ou expiré.";

    private readonly IJwtGeneratorService _jwtGeneratorService;
    private readonly IUserRoleDataService _userRoleDataService;
    private readonly ISpaceRoleResolverService _spaceRoleResolverService;
    private readonly ISectionVoicePartLookupService _sectionVoicePartLookupService;
    private readonly IEmailService _emailService;

    public AccountService(
        IServiceProvider serviceProvider,
        IJwtGeneratorService jwtGeneratorService,
        IUserRoleDataService userRoleDataService,
        ISpaceRoleResolverService spaceRoleResolverService,
        ISectionVoicePartLookupService sectionVoicePartLookupService,
        IEmailService emailService)
        : base(serviceProvider)
    {
        _jwtGeneratorService = jwtGeneratorService;
        _userRoleDataService = userRoleDataService;
        _spaceRoleResolverService = spaceRoleResolverService;
        _sectionVoicePartLookupService = sectionVoicePartLookupService;
        _emailService = emailService;
    }

    public async Task<TokenViewModel?> Login(LoginViewModel model)
        => await LoginWithPasswordInternal(model);

    public async Task<TokenViewModel?> RefreshTokenAsync(TokenViewModel request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            throw new CustomException(HttpStatusCode.BadRequest, "Refresh token requis.");

        try
        {
            var (access, refresh) = await _jwtGeneratorService.RotateRefresh(
                request.RefreshToken, request.DeviceId);
            return new TokenViewModel { AccessToken = access, RefreshToken = refresh.Token };
        }
        catch (SecurityException)
        {
            return null;
        }
    }

    public async Task UnlockUser(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId)
            ?? throw new KeyNotFoundException($"User {userId} not found.");
        await _userManager.SetLockoutEndDateAsync(user, null);
        await _userManager.ResetAccessFailedCountAsync(user);
    }

    public async Task<IdentityResult> ResetPassword(ResetPasswordRequestViewModel model)
    {
        var user = await _userManager.FindByIdAsync(model.UserId)
            ?? throw new KeyNotFoundException($"User {model.UserId} not found.");

        // Jeton illisible : trace explicite AVANT le refus. Sans elle, un lien casse a
        // l'emission et une faute de frappe de l'utilisateur produisent le meme 400 muet —
        // c'est ce qui a laisse vivre le defaut de remplissage base64url sans aucun signal.
        if (!UrlTokenHelper.TryDecode(model.Token, out var token))
        {
            _logger.LogWarning(
                $"ResetPassword : jeton illisible (base64url invalide) pour l'utilisateur {model.UserId}.");
            throw new CustomException(
                HttpStatusCode.BadRequest, "Lien de réinitialisation invalide ou expiré.");
        }

        var result = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);

        if (result.Succeeded && (!user.EmailConfirmed || user.IsGuestAccount))
        {
            // Ce lien est celui envoye a l'invitation : le suivre pour la premiere fois est
            // la revendication du compte. Sans desactiver IsGuestAccount ici, l'invite
            // converti restait indefiniment eligible a l'anonymisation par
            // GuestAccountLifecycleService, dont le filtre est IsGuestAccount && !EmailConfirmed.
            user.EmailConfirmed = true;
            user.IsGuestAccount = false;
            await _context.SaveChangesAsync();
        }

        return result;
    }

    public async Task ActivateAccountAsync(ActivateAccountViewModel model, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(model.UserId);

        // Utilisateur inconnu et jeton illisible produisent la MEME reponse : ce lien est
        // public, distinguer les deux transformerait l'endpoint en oracle d'existence de
        // compte. La trace, elle, distingue — c'est cote serveur qu'on diagnostique.
        if (user is null)
        {
            _logger.LogWarning(
                $"ActivateAccount : utilisateur {model.UserId} introuvable.");
            throw new CustomException(HttpStatusCode.BadRequest, InvalidActivationLinkMessage);
        }

        if (!UrlTokenHelper.TryDecode(model.Token, out var token))
        {
            _logger.LogWarning(
                $"ActivateAccount : jeton illisible (base64url invalide) pour l'utilisateur {model.UserId}.");
            throw new CustomException(HttpStatusCode.BadRequest, InvalidActivationLinkMessage);
        }

        var isTokenValid = await _userManager.VerifyUserTokenAsync(
            user,
            AccountTokenConstants.InvitationTokenProvider,
            AccountTokenConstants.AccountActivationPurpose,
            token);

        if (!isTokenValid)
            throw new CustomException(HttpStatusCode.BadRequest, InvalidActivationLinkMessage);

        await SetPasswordAsync(user, model.NewPassword);

        // Meme revendication que ResetPassword sur un lien d'invitation : sans desactiver
        // IsGuestAccount, l'invite converti resterait eligible a l'anonymisation par
        // GuestAccountLifecycleService, dont le filtre est IsGuestAccount && !EmailConfirmed.
        user.EmailConfirmed = true;
        user.IsGuestAccount = false;
        await _context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Remplace le mot de passe temporaire du compte invité par celui choisi.
    /// </summary>
    /// <remarks>
    /// La validation précède volontairement le retrait : <c>AddPasswordAsync</c> applique les
    /// règles de complexité et peut échouer. Retirer d'abord laisserait, sur un refus, un
    /// compte sans aucun mot de passe — donc inaccessible autrement que par un nouveau lien.
    /// </remarks>
    private async Task SetPasswordAsync(User user, string newPassword)
    {
        var validationErrors = new List<string>();
        foreach (var validator in _userManager.PasswordValidators)
        {
            var validation = await validator.ValidateAsync(_userManager, user, newPassword);
            if (!validation.Succeeded)
                validationErrors.AddRange(validation.Errors.Select(e => e.Description));
        }

        if (validationErrors.Count > 0)
            throw new CustomException(
                HttpStatusCode.BadRequest, "Mot de passe refusé.", validationErrors.Distinct().ToList());

        if (await _userManager.HasPasswordAsync(user))
        {
            var removeResult = await _userManager.RemovePasswordAsync(user);
            if (!removeResult.Succeeded)
                throw new CustomException(
                    HttpStatusCode.BadRequest,
                    "Impossible de définir le mot de passe.",
                    removeResult.Errors.Select(e => e.Description).ToList());
        }

        var addResult = await _userManager.AddPasswordAsync(user, newPassword);
        if (!addResult.Succeeded)
            throw new CustomException(
                HttpStatusCode.BadRequest,
                "Impossible de définir le mot de passe.",
                addResult.Errors.Select(e => e.Description).ToList());
    }

    public async Task Logout(LogoutRequestViewModel? request)
    {
        if (request?.RefreshToken is not null)
            await _jwtGeneratorService.RevokeRefreshAsync(request.RefreshToken);
    }

    public async Task ForgotPassword(string email)
    {
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == email);

        if (user is null)
        {
            await Task.Delay(Random.Shared.Next(200, 500));
            return;
        }

        var tokenBase64 = UrlTokenHelper.Encode(
            await _userManager.GeneratePasswordResetTokenAsync(user));

        var frontUrl = _configuration["Frontend:BaseUrl"]
            ?? throw new InvalidOperationException("Frontend:BaseUrl manquant.");
        var resetLink = $"{frontUrl}/reset-password?userId={user.Id}&token={tokenBase64}";

        await _emailService.SendAsync(
            email,
            "Réinitialisation de votre mot de passe",
            $"<p>Cliquez sur le lien suivant pour réinitialiser votre mot de passe : <a href=\"{resetLink}\">{resetLink}</a></p>");
    }

    public async Task<AuthenticatedUserViewModel> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        if (_currentUserId is null)
            throw new CustomException(HttpStatusCode.Unauthorized, "Non authentifié.");

        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == _currentUserId, cancellationToken)
            ?? throw new KeyNotFoundException("Utilisateur not found.");

        var roles = await _userRoleDataService.GetUserRolesAsync(_currentUserId);
        var spaceRoles = await BuildSpaceRolesAsync(_currentUserId, cancellationToken);
        var clientRoles = await BuildClientRolesAsync(_currentUserId, cancellationToken);

        return new AuthenticatedUserViewModel
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            Firstname = user.Firstname,
            Lastname = user.Lastname,
            Roles = roles,
            SpaceRoles = spaceRoles,
            ClientRoles = clientRoles
        };
    }

    /// <summary>
    /// Tous les espaces (chorales et events confondus) ou l'utilisateur a un
    /// <see cref="SpaceMember"/> Active, avec ses roles effectifs.
    /// </summary>
    /// <remarks>
    /// <see cref="ISpaceRoleResolverService.ResolveRolesAsync"/> filtre desormais lui-meme
    /// sur <see cref="MemberStatusEnum.Active"/> — ce filtre ici, en amont, est donc redondant
    /// avec le sien. On le conserve volontairement : il permet un retour anticipe (`[]`) sans
    /// meme appeler le resolveur quand l'utilisateur n'a aucune appartenance actif, et sert
    /// de filet si le contrat du resolveur venait a change pour un autre appelant. Les deux
    /// filtres doivent toujours produire exactement le meme ensemble d'espaces.
    ///
    /// Count de requetes constant, quel que soit le nombre d'espaces de l'utilisateur :
    /// une pour les appartenances actives, celles internes a <c>ResolveRolesAsync</c>, puis
    /// une pour les espaces, une pour les chorales, une pour les events et une pour les
    /// voix principales (<see cref="ISectionVoicePartLookupService"/>) — jamais une requete
    /// par espace.
    /// </remarks>
    private async Task<List<SpaceRoleAssignmentViewModel>> BuildSpaceRolesAsync(
        string userId, CancellationToken cancellationToken)
    {
        var spaceIdsActive = await _context.SpaceMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.Status == MemberStatusEnum.Active)
            .Select(m => m.SpaceId)
            .ToListAsync(cancellationToken);

        if (spaceIdsActive.Count == 0)
            return [];

        var rolesBySpace = await _spaceRoleResolverService.ResolveRolesAsync(
            userId, spaceIdsActive, cancellationToken);
        if (rolesBySpace.Count == 0)
            return [];

        var spaceIds = rolesBySpace.Keys.ToList();

        var spaces = await _context.Spaces
            .AsNoTracking()
            .Where(e => spaceIds.Contains(e.Id) && !e.IsDeleted)
            .Select(e => new { e.Id, e.SpaceType, e.ClientId })
            .ToListAsync(cancellationToken);

        if (spaces.Count == 0)
            return [];

        var spacesIds = spaces.Select(e => e.Id).ToList();

        var choirNamesById = await _context.Choirs
            .AsNoTracking()
            .Where(c => spacesIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        var eventsById = await _context.Events
            .AsNoTracking()
            .Where(e => spacesIds.Contains(e.Id))
            .Select(e => new { e.Id, e.Title, e.ChoirId })
            .ToDictionaryAsync(e => e.Id, e => e, cancellationToken);

        var choirSpaceIds = spaces
            .Where(s => s.SpaceType == SpaceTypeEnum.Choir)
            .Select(s => s.Id)
            .ToList();

        var primaryVoicePartByChoirId = await _sectionVoicePartLookupService
            .GetPrimaryVoicePartsAsync(userId, choirSpaceIds, cancellationToken);

        var result = new List<SpaceRoleAssignmentViewModel>();
        foreach (var space in spaces)
        {
            var name = space.SpaceType == SpaceTypeEnum.Choir
                ? choirNamesById.GetValueOrDefault(space.Id)
                : eventsById.GetValueOrDefault(space.Id)?.Title;

            if (name is null)
                continue;

            result.Add(new SpaceRoleAssignmentViewModel
            {
                SpaceId = space.Id,
                Name = name,
                SpaceType = space.SpaceType,
                Roles = rolesBySpace[space.Id].Select(r => r.ToString()).ToList(),
                ClientId = space.ClientId,
                ChoirId = space.SpaceType == SpaceTypeEnum.Event
                    ? eventsById.GetValueOrDefault(space.Id)?.ChoirId
                    : null,
                // GetValueOrDefault interdit ici : VoicePartEnum est un type valeur dont le
                // default (0) vaut Alto — un membre sans pupitre se retrouverait affiche avec
                // la voix Alto au lieu de n'en avoir aucune. TryGetValue est la seule facon de
                // distinguer "absent" de "Alto".
                PrimaryVoicePart = space.SpaceType == SpaceTypeEnum.Choir
                    && primaryVoicePartByChoirId.TryGetValue(space.Id, out var voicePart)
                    ? voicePart
                    : null
            });
        }

        return result;
    }

    /// <summary>
    /// Rattachements client (<see cref="ClientMember"/>) de l'utilisateur, sans filtre sur
    /// <see cref="ClientStatusEnum"/> — a la difference des espaces, un responsable client
    /// doit voir son rattachement meme si le client est Suspendu ou Archive : c'est cette
    /// zone qui lui permet de constater la suspension, pas une zone qu'elle doit lui
    /// masquer.
    /// </summary>
    private async Task<List<ClientRoleAssignmentViewModel>> BuildClientRolesAsync(
        string userId, CancellationToken cancellationToken)
    {
        var members = await _context.ClientMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => new { m.ClientId, m.Client.Name, m.Role })
            .ToListAsync(cancellationToken);

        return members
            .GroupBy(m => new { m.ClientId, m.Name })
            .Select(g => new ClientRoleAssignmentViewModel
            {
                ClientId = g.Key.ClientId,
                Name = g.Key.Name,
                Roles = g.Select(m => m.Role.ToString()).Distinct().ToList()
            })
            .ToList();
    }

    private async Task<TokenViewModel?> LoginWithPasswordInternal(LoginViewModel model)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email == model.Email);

        if (user is null || user.IsDeleted || !user.IsActive)
            throw new CustomException(HttpStatusCode.Unauthorized, "Identifiants invalides.");

        if (await _userManager.IsLockedOutAsync(user))
            throw new CustomException(HttpStatusCode.TooManyRequests, "Compte temporairement verrouillé.");

        var result = await _signInManager.CheckPasswordSignInAsync(user, model.Password, true);

        if (!result.Succeeded)
            throw new CustomException(HttpStatusCode.Unauthorized, "Identifiants invalides.");

        if (await IsInvitedAccessExpiredAsync(user))
            throw new CustomException(HttpStatusCode.Unauthorized, "Identifiants invalides.");

        await _userManager.ResetAccessFailedCountAsync(user);
        user.LastConnection = DateTime.UtcNow;
        user.LastActive = DateTime.UtcNow;

        await PromoteInvitedMembershipsAsync(user.Id);

        await _context.SaveChangesAsync();

        var access = await _jwtGeneratorService.GenerateJwtToken(user);
        var refresh = await _jwtGeneratorService.GenerateUserRefreshToken(user, model.DeviceId);

        return new TokenViewModel
        {
            AccessToken = access,
            RefreshToken = refresh.Token,
            DeviceId = model.DeviceId
        };
    }

    /// <summary>
    /// Fait passer les appartenances de <c>Invite</c> à <c>Active</c> à la connexion.
    /// </summary>
    /// <remarks>
    /// <c>04</c> § Membre : « invité — créé avant sa première connexion. Réversible : oui,
    /// vers actif à la connexion ». Cette transition n'existait nulle part dans le code : un
    /// membre invité restait <c>Invite</c> indéfiniment.
    ///
    /// Tant que l'appartenance n'était vérifiée que par un test de présence, cela ne se
    /// voyait pas. Depuis que l'accès au contenu exige <c>Active</c>, son absence
    /// verrouillerait tout nouveau membre hors de sa chorale — d'où sa livraison dans le
    /// même lot que <see cref="IMembershipService"/>, et pas après.
    ///
    /// Ne touche pas aux appartenances <c>Inactive</c> ni <c>Archive</c> : ce sont des accès
    /// délibérément révoqués, qu'une simple connexion ne doit pas rétablir.
    /// </remarks>
    private async Task PromoteInvitedMembershipsAsync(string userId)
    {
        var invitations = await _context.SpaceMembers
            .Where(m => m.UserId == userId && m.Status == MemberStatusEnum.Invited)
            .ToListAsync();

        foreach (var membership in invitations)
            membership.Status = MemberStatusEnum.Active;
    }

    private async Task<bool> IsInvitedAccessExpiredAsync(User user)
    {
        if (!user.IsGuestAccount || user.EmailConfirmed) return false;

        var spaceIds = await _context.SpaceMembers
            .AsNoTracking()
            .Where(m => m.UserId == user.Id && !m.IsDeleted)
            .Select(m => m.SpaceId)
            .ToListAsync();

        if (spaceIds.Count == 0) return true;

        var spaces = await _context.Spaces
            .AsNoTracking()
            .Where(e => spaceIds.Contains(e.Id))
            .Select(e => new { e.Id, e.SpaceType })
            .ToListAsync();

        if (spaces.Any(e => e.SpaceType == SpaceTypeEnum.Choir)) return false;

        var eventIds = spaces
            .Where(e => e.SpaceType == SpaceTypeEnum.Event)
            .Select(e => e.Id)
            .ToList();

        if (eventIds.Count == 0) return true;

        var events = await _context.Events
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(e => eventIds.Contains(e.Id))
            .Select(e => new { e.IsDeleted, e.StartDate, e.EndDate })
            .ToListAsync();

        return events.Count > 0
            && events.All(e => e.IsDeleted || EventStateHelper.IsFinished(e.StartDate, e.EndDate));
    }
}
