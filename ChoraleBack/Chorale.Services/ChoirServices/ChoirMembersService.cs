using System.Linq.Expressions;
using System.Net;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Helpers;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Services.UserServices;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.ChoirMembers;
using Microsoft.EntityFrameworkCore;

namespace ChoraleBackEnd.Services.ChoirServices;

public interface IChoirMembersService
{
    Task<PagedListViewModel<ChoirMemberListItemViewModel>> GetPagedAsync(
        Guid choirId, PaginateViewModel pagination, CancellationToken ct = default);
    Task<ChoirMemberListItemViewModel> GetByIdAsync(Guid spaceId, Guid id, CancellationToken ct = default);
    Task<ChoirMemberListItemViewModel> InviteAsync(Guid spaceId, InviteMemberViewModel model, CancellationToken ct = default);
    Task<ChoirMemberListItemViewModel> UpdateAsync(Guid spaceId, UpdateChoirMemberViewModel model, CancellationToken ct = default);
    Task<ChoirMemberListItemViewModel> ChangeRoleAsync(Guid spaceId, ChangeMemberRoleViewModel model, CancellationToken ct = default);
    Task<ChoirMemberListItemViewModel> ChangeStatusAsync(Guid spaceId, ChangeMemberStatusViewModel model, CancellationToken ct = default);
}

public sealed class ChoirMembersService : BaseService, IChoirMembersService
{
    // Liste blanche de tri : sans elle, Skip/Take sur une requete non triee n'est pas
    // reproductible — voir TriHelper. Tri par defaut nom puis prenom, departage sur Id.
    private static readonly IReadOnlyDictionary<string, Expression<Func<SpaceMember, object?>>> MembersSortableColumns =
        new Dictionary<string, Expression<Func<SpaceMember, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = m => m.User.Lastname,
            ["FirstName"] = m => m.User.Firstname,
            // Email absent de cette liste : la colonne Email de l'ecran Membres etait
            // cliquable mais son tri retombait silencieusement sur l'ordre par defaut.
            ["Email"] = m => m.User.Email,
            ["Status"] = m => m.Status
        };

    private readonly ISectionService _sectionService;
    private readonly IAuditLogService _auditLogService;
    private readonly IServiceLimitService _serviceLimitService;
    private readonly IMembershipService _membershipService;
    private readonly IUserInvitationService _userInvitationService;
    private readonly IMemberEnrollmentService _memberEnrollmentService;
    private readonly ISectionVoicePartLookupService _sectionVoicePartLookupService;

    public ChoirMembersService(
        IServiceProvider serviceProvider,
        ISectionService sectionService,
        IAuditLogService auditLogService,
        IServiceLimitService serviceLimitService,
        IMembershipService membershipService,
        IUserInvitationService userInvitationService,
        IMemberEnrollmentService memberEnrollmentService,
        ISectionVoicePartLookupService sectionVoicePartLookupService)
        : base(serviceProvider)
    {
        _sectionService = sectionService;
        _auditLogService = auditLogService;
        _serviceLimitService = serviceLimitService;
        _membershipService = membershipService;
        _userInvitationService = userInvitationService;
        _memberEnrollmentService = memberEnrollmentService;
        _sectionVoicePartLookupService = sectionVoicePartLookupService;
    }

    /// <summary>
    /// Verifie que la ressource visee appartient bien a l'espace autorise.
    /// </summary>
    /// <remarks>
    /// Un Admin global peut franchir cette frontiere au titre du support. Dans ce cas
    /// l'acces est <b>trace</b>, jamais silencieux : `02` §66 impose que tout acces de
    /// l'administration generale aux donnees d'une chorale laisse une trace. La trace est
    /// ajoutee au contexte, donc committee dans la meme transaction que l'operation —
    /// il n'existe pas de chemin ou la donnee part sans que la trace soit ecrite.
    /// </remarks>
    private void EnsureScopeSpace(
        Guid? resourceChoirId, Guid spaceId, string action, string entityId)
    {
        if (resourceChoirId == spaceId)
            return;

        if (!_currentUserRoles.Contains(UserRoleEnum.Admin))
            throw new CustomException(HttpStatusCode.Forbidden, "Accès refusé à ce membre.");

        _auditLogService.Record(
            action,
            nameof(SpaceMember),
            entityId,
            $"Accès support hors périmètre : espace autorisé {spaceId}, "
            + $"ressource rattachée à {resourceChoirId?.ToString() ?? "aucune chorale"}.");
    }

    public async Task<PagedListViewModel<ChoirMemberListItemViewModel>> GetPagedAsync(
        Guid choirId, PaginateViewModel pagination, CancellationToken ct = default)
    {
        var query = _context.SpaceMembers
            .AsNoTracking()
            .Include(m => m.User)
            .Where(m => m.ChoirId == choirId);

        if (!string.IsNullOrWhiteSpace(pagination.Filter))
            query = query.Where(m =>
                m.User.Firstname.Contains(pagination.Filter) ||
                m.User.Lastname.Contains(pagination.Filter) ||
                m.User.Email != null && m.User.Email.Contains(pagination.Filter));

        var total = await query.CountAsync(ct);
        var items = await query
            .ApplySort(
                pagination.SortActive, pagination.SortDirection, MembersSortableColumns, m => m.Id,
                q => q.OrderBy(m => m.User.Lastname).ThenBy(m => m.User.Firstname).ThenBy(m => m.Id))
            .Skip(pagination.Offset)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        var viewModels = _mapper.Map<List<ChoirMemberListItemViewModel>>(items);
        await EnrichMemberDetailsAsync(viewModels, ct);

        return new PagedListViewModel<ChoirMemberListItemViewModel>
        {
            Items = viewModels,
            TotalCount = total,
            CurrentPage = pagination.Page,
            PageSize = pagination.PageSize
        };
    }

    public async Task<ChoirMemberListItemViewModel> GetByIdAsync(Guid spaceId, Guid id, CancellationToken ct = default)
    {
        var member = await _context.SpaceMembers
            .AsNoTracking()
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new KeyNotFoundException($"Member {id} not found.");

        EnsureScopeSpace(member.ChoirId, spaceId, "MemberViewed", member.Id.ToString());

        var viewModel = _mapper.Map<ChoirMemberListItemViewModel>(member);
        await EnrichMemberDetailsAsync([viewModel], ct);
        return viewModel;
    }

    public async Task<ChoirMemberListItemViewModel> InviteAsync(Guid spaceId, InviteMemberViewModel model, CancellationToken ct = default)
    {
        EnsureScopeSpace(model.ChoirId, spaceId, "MemberInvited", model.ChoirId.ToString());
        await _membershipService.EnsureCanWriteAsync(model.ChoirId, ct);

        // Plafond de membres du client. Il porte sur l'ensemble du client et non sur une
        // chorale : sinon il suffirait de repartir les membres sur plusieurs chorales.
        await _serviceLimitService.EnsureCanAddMemberAsync(model.ChoirId, ct);

        var existingUser = await _userManager.FindByEmailAsync(model.Email);

        var member = existingUser is null
            ? await InviteNewUserAsync(model, ct)
            : await InviteExistingUserAsync(existingUser, model, ct);

        var reloaded = await _context.SpaceMembers
            .AsNoTracking()
            .Include(m => m.User)
            .FirstAsync(m => m.Id == member.Id, ct);

        var viewModel = _mapper.Map<ChoirMemberListItemViewModel>(reloaded);
        await EnrichMemberDetailsAsync([viewModel], ct);
        return viewModel;
    }

    public async Task<ChoirMemberListItemViewModel> UpdateAsync(Guid spaceId, UpdateChoirMemberViewModel model, CancellationToken ct = default)
    {
        var member = await _context.SpaceMembers
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.Id == model.Id, ct)
            ?? throw new KeyNotFoundException($"Member {model.Id} not found.");

        EnsureScopeSpace(member.ChoirId, spaceId, "MemberUpdated", member.Id.ToString());
        await EnsureWriteMemberAsync(member.ChoirId, ct);

        var renameRequested = !string.IsNullOrWhiteSpace(model.Firstname) || !string.IsNullOrWhiteSpace(model.Lastname);

        // Le nom/prenom vit sur le compte User, global et unique par email — pas sur
        // l'appartenance a CETTE chorale. Un compte deja active (EmailConfirmed) peut etre
        // rattache a plusieurs clients avec la meme identite : un chef de chœur qui le
        // renomme le renommerait PARTOUT. Des lors qu'un compte est active, seule la personne
        // concernee gere son propre nom. Aucun bypass Admin : l'administration dispose deja
        // de AdminUserService.UpdateIdentityAsync pour un renommage legitime, ce chemin-ci
        // (chef de chœur, appartenance chorale) n'a pas a etre affaibli pour elle.
        if (renameRequested && member.User.EmailConfirmed)
            throw new CustomException(HttpStatusCode.Forbidden,
                "Ce compte est déjà activé : seule la personne concernée peut modifier son nom.");

        if (!string.IsNullOrWhiteSpace(model.Firstname))
            member.User.Firstname = model.Firstname;
        if (!string.IsNullOrWhiteSpace(model.Lastname))
            member.User.Lastname = model.Lastname;

        await _context.SaveChangesAsync(ct);

        var viewModel = _mapper.Map<ChoirMemberListItemViewModel>(member);
        await EnrichMemberDetailsAsync([viewModel], ct);
        return viewModel;
    }

    public async Task<ChoirMemberListItemViewModel> ChangeRoleAsync(Guid spaceId, ChangeMemberRoleViewModel model, CancellationToken ct = default)
    {
        var member = await _context.SpaceMembers
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.Id == model.Id, ct)
            ?? throw new KeyNotFoundException($"Member {model.Id} not found.");

        EnsureScopeSpace(member.ChoirId, spaceId, "MemberRoleChanged", member.Id.ToString());
        await EnsureWriteMemberAsync(member.ChoirId, ct);

        switch (model.Role)
        {
            case UserRoleEnum.Manager:
                await AssignManagerRoleAsync(member.Id, ct);
                break;
            case UserRoleEnum.Singer:
                await RevokeManagerRoleAsync(member, ct);
                break;
            case UserRoleEnum.SectionLeader:
                await AssignSectionLeaderRoleAsync(member, ct);
                break;
            default:
                throw new CustomException(HttpStatusCode.BadRequest, "Rôle non supporté.");
        }

        var viewModel = _mapper.Map<ChoirMemberListItemViewModel>(member);
        await EnrichMemberDetailsAsync([viewModel], ct);
        return viewModel;
    }

    public async Task<ChoirMemberListItemViewModel> ChangeStatusAsync(Guid spaceId, ChangeMemberStatusViewModel model, CancellationToken ct = default)
    {
        var member = await _context.SpaceMembers
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.Id == model.Id, ct)
            ?? throw new KeyNotFoundException($"Member {model.Id} not found.");

        EnsureScopeSpace(member.ChoirId, spaceId, "MemberStatusChanged", member.Id.ToString());
        await EnsureWriteMemberAsync(member.ChoirId, ct);

        switch (model.Status)
        {
            case MemberStatusEnum.Archived:
                await ArchiveMemberAsync(member, ct);
                break;
            case MemberStatusEnum.Inactive:
                // Meme invariant que l'archivage (ArchiveMemberAsync) : passer le dernier
                // Manager en Inactive le sort tout autant du decompte des managers actifs
                // (IMembershipService.EnsureNotLastManagerAsync filtre Status == Active) —
                // recreerait la meme impasse par une porte differente.
                await EnsureNotLastManagerIfManagerAsync(member, ct);
                member.Status = model.Status;
                await _context.SaveChangesAsync(ct);
                break;
            case MemberStatusEnum.Active:
                member.Status = model.Status;
                await _context.SaveChangesAsync(ct);
                break;
            case MemberStatusEnum.Invited:
                throw new CustomException(
                    HttpStatusCode.BadRequest,
                    "Ce statut n'est atteignable que par la création via invitation.");
            default:
                throw new CustomException(HttpStatusCode.BadRequest, "Statut non supporté.");
        }

        var viewModel = _mapper.Map<ChoirMemberListItemViewModel>(member);
        await EnrichMemberDetailsAsync([viewModel], ct);
        return viewModel;
    }

    /// <summary>
    /// Ferme l'ecriture sur un membre des lors que sa chorale n'accepte plus le contenu —
    /// voir <see cref="IMembershipService.EnsureCanWriteAsync"/>. Un membre sans
    /// <c>ChoirId</c> (rattache a un autre type d'espace) n'est pas concerne.
    /// </summary>
    private async Task EnsureWriteMemberAsync(Guid? choirId, CancellationToken ct)
    {
        if (choirId.HasValue)
            await _membershipService.EnsureCanWriteAsync(choirId.Value, ct);
    }

    /// <summary>
    /// Compte inconnu : la création du compte invité et l'envoi du lien appartiennent à
    /// <see cref="IUserInvitationService"/>.
    /// </summary>
    /// <remarks>
    /// Cette branche réimplémentait la même séquence en oubliant <c>IsGuestAccount</c> : les
    /// comptes créés par ce chemin échappaient au cycle de vie des invités
    /// (<c>GuestAccountLifecycleService</c>) et n'étaient donc jamais purgés.
    /// </remarks>
    private async Task<SpaceMember> InviteNewUserAsync(InviteMemberViewModel model, CancellationToken ct)
    {
        var choirName = await _context.Choirs
            .AsNoTracking()
            .Where(c => c.Id == model.ChoirId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"Choir {model.ChoirId} not found.");

        var user = await _userInvitationService.InviteGuestAsync(
            model.Email, model.Firstname, SpaceTypeEnum.Choir, choirName, model.Lastname, ct);

        var member = await _memberEnrollmentService.EnrollAsync(
            model.ChoirId, user.Id, MemberStatusEnum.Invited, model.PrimaryVoicePart, role: null, ct);

        await _context.SaveChangesAsync(ct);
        return member;
    }

    /// <summary>
    /// Compte déjà existant : rattachement direct.
    /// </summary>
    /// <remarks>
    /// Volontairement distinct de <see cref="InviteNewUserAsync"/> :
    /// <c>InviteGuestAsync</c> refuse en 409 un compte actif et revendiqué, alors qu'inviter
    /// un membre qui a déjà un compte doit précisément le rattacher.
    /// </remarks>
    private async Task<SpaceMember> InviteExistingUserAsync(
        User existingUser, InviteMemberViewModel model, CancellationToken ct)
    {
        var alreadyMember = await _context.SpaceMembers
            .AnyAsync(m => m.UserId == existingUser.Id && m.ChoirId == model.ChoirId, ct);

        if (alreadyMember)
            throw new CustomException(
                "Utilisateur déjà membre actif de cette chorale.",
                "Ce membre appartient déjà à cette chorale.",
                HttpStatusCode.Conflict);

        var member = await _memberEnrollmentService.EnrollAsync(
            model.ChoirId, existingUser.Id, MemberStatusEnum.Active, model.PrimaryVoicePart, role: null, ct);

        await _context.SaveChangesAsync(ct);
        return member;
    }

    private async Task AssignManagerRoleAsync(Guid spaceMemberId, CancellationToken ct)
    {
        var exists = await _context.SpaceMemberRoles
            .AnyAsync(r => r.SpaceMemberId == spaceMemberId && r.Role == UserRoleEnum.Manager, ct);

        if (exists) return;

        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SpaceMemberId = spaceMemberId,
            Role = UserRoleEnum.Manager
        });
        await _context.SaveChangesAsync(ct);
    }

    private async Task RevokeManagerRoleAsync(SpaceMember member, CancellationToken ct)
    {
        var isSectionLeader = member.ChoirId is { } choirId
            && await _sectionService.IsSectionLeaderInChoirAsync(choirId, member.UserId, ct);

        if (isSectionLeader)
            throw new CustomException(
                HttpStatusCode.BadRequest,
                "Retirez d'abord le rôle de chef de pupitre avant de repasser choriste.");

        var role = await _context.SpaceMemberRoles
            .FirstOrDefaultAsync(r => r.SpaceMemberId == member.Id && r.Role == UserRoleEnum.Manager, ct);

        if (role is null) return;

        // Point unique de l'invariant "au moins un Manager actif" — voir
        // IMembershipService.EnsureNotLastManagerAsync.
        if (member.ChoirId is { } revokeChoirId)
            await _membershipService.EnsureNotLastManagerAsync(revokeChoirId, member.Id, ct);

        _context.SpaceMemberRoles.Remove(role);
        await _context.SaveChangesAsync(ct);
    }

    private async Task AssignSectionLeaderRoleAsync(SpaceMember member, CancellationToken ct)
    {
        var sectionId = await _context.SectionMembers
            .AsNoTracking()
            .Include(mp => mp.Section)
            .Where(mp => mp.UserId == member.UserId && mp.Section.ChoirId == member.ChoirId)
            .Select(mp => (Guid?)mp.SectionId)
            .FirstOrDefaultAsync(ct);

        if (sectionId is null)
            throw new CustomException(
                HttpStatusCode.BadRequest,
                "Le membre doit avoir une voix affectée avant d'être nommé chef de pupitre.");

        await _sectionService.UpdateLeaderAsync(sectionId.Value, member.UserId, ct);
    }

    private async Task ArchiveMemberAsync(SpaceMember member, CancellationToken ct)
    {
        var isSectionLeader = member.ChoirId is { } choirId
            && await _sectionService.IsSectionLeaderInChoirAsync(choirId, member.UserId, ct);

        if (isSectionLeader)
            throw new CustomException(
                HttpStatusCode.BadRequest,
                "Retirez d'abord le rôle de chef de pupitre avant d'archiver.");

        await EnsureNotLastManagerIfManagerAsync(member, ct);

        member.IsDeleted = true;
        member.Status = MemberStatusEnum.Archived;
        await _context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Verifie l'invariant "au moins un Manager actif" AVANT de faire perdre son statut Actif
    /// au membre visé (archivage ou passage Inactive) — appelée uniquement s'il porte
    /// effectivement le rôle Manager, sinon aucune des deux transitions n'affecte le décompte.
    /// </summary>
    private async Task EnsureNotLastManagerIfManagerAsync(SpaceMember member, CancellationToken ct)
    {
        if (member.ChoirId is not { } choirId) return;

        var isManager = await _context.SpaceMemberRoles
            .AnyAsync(r => r.SpaceMemberId == member.Id && r.Role == UserRoleEnum.Manager, ct);

        if (isManager)
            await _membershipService.EnsureNotLastManagerAsync(choirId, member.Id, ct);
    }

    private async Task EnrichMemberDetailsAsync(List<ChoirMemberListItemViewModel> viewModels, CancellationToken ct)
    {
        await AttachRolesAsync(viewModels, ct);
        await AttachSectionAsync(viewModels, ct);
    }

    private async Task AttachSectionAsync(List<ChoirMemberListItemViewModel> viewModels, CancellationToken ct)
    {
        if (viewModels.Count == 0) return;

        var userIds = viewModels.Select(v => v.UserId).Distinct().ToList();
        var choirIds = viewModels.Select(v => v.ChoirId).Distinct().ToList();

        var sectionLookup = await _sectionVoicePartLookupService.GetPrimarySectionsAsync(userIds, choirIds, ct);

        foreach (var viewModel in viewModels)
        {
            if (!sectionLookup.TryGetValue((viewModel.UserId, viewModel.ChoirId), out var section)) continue;

            viewModel.SectionId = section.SectionId;
            viewModel.SectionVoicePart = section.VoicePart;
        }
    }

    private async Task AttachRolesAsync(List<ChoirMemberListItemViewModel> viewModels, CancellationToken ct)
    {
        if (viewModels.Count == 0) return;

        var ids = viewModels.Select(v => v.Id).ToList();
        var rolesById = await _context.SpaceMemberRoles
            .AsNoTracking()
            .Where(r => ids.Contains(r.SpaceMemberId))
            .GroupBy(r => r.SpaceMemberId)
            .Select(g => new { SpaceMemberId = g.Key, Roles = g.Select(r => r.Role).ToList() })
            .ToListAsync(ct);

        var sectionLeaderUserIds = await _context.Sections
            .AsNoTracking()
            .Where(p => p.SectionLeaderId != null)
            .Select(p => new { p.ChoirId, p.SectionLeaderId })
            .ToListAsync(ct);

        var rolesLookup = rolesById.ToDictionary(r => r.SpaceMemberId, r => r.Roles);

        foreach (var viewModel in viewModels)
        {
            var roles = new List<UserRoleEnum> { UserRoleEnum.Singer };

            if (rolesLookup.TryGetValue(viewModel.Id, out var extraRoles))
                roles.AddRange(extraRoles);

            if (sectionLeaderUserIds.Any(p => p.ChoirId == viewModel.ChoirId && p.SectionLeaderId == viewModel.UserId))
                roles.Add(UserRoleEnum.SectionLeader);

            viewModel.Roles = roles.Distinct().Select(r => r.ToString()).ToList();
        }
    }

}
