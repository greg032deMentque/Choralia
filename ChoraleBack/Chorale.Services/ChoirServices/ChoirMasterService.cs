using System.Net;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.ChoirMembers;
using Microsoft.EntityFrameworkCore;

namespace ChoraleBackEnd.Services.ChoirServices;

/// <summary>
/// Gere le chef de chœur (role <c>Manager</c>) d'une chorale EXISTANTE pour le compte d'un
/// ResponsableClient qui n'est pas necessairement membre de cette chorale — a la difference de
/// <see cref="IChoirMembersService"/>, dont chaque methode exige une appartenance active
/// (<c>EnsureCanWriteAsync</c>). Autorite : role client (<c>ClientRoleResolverService</c>),
/// jamais l'appartenance a la chorale. Sert notamment a depanner une chorale amorcee par
/// <c>ChoirService.CreateAsync</c> dont le seul chef de chœur aurait ete retire ailleurs.
/// </summary>
public interface IChoirMasterService
{
    Task<PagedListViewModel<ChoirMemberListItemViewModel>> GetPagedAsync(
        Guid choirId, PaginateViewModel pagination, CancellationToken ct = default);
    Task<ChoirMemberListItemViewModel> AssignAsync(
        Guid choirId, AssignChoirMasterViewModel model, CancellationToken ct = default);
    Task RevokeAsync(Guid choirId, string userId, CancellationToken ct = default);
}

public sealed class ChoirMasterService : BaseService, IChoirMasterService
{
    private readonly IAuditLogService _auditLogService;
    private readonly IServiceLimitService _serviceLimitService;
    private readonly IClientRoleResolverService _clientRoleResolverService;
    private readonly ISectionService _sectionService;
    private readonly IMembershipService _membershipService;

    public ChoirMasterService(
        IServiceProvider serviceProvider,
        IAuditLogService auditLogService,
        IServiceLimitService serviceLimitService,
        IClientRoleResolverService clientRoleResolverService,
        ISectionService sectionService,
        IMembershipService membershipService)
        : base(serviceProvider)
    {
        _auditLogService = auditLogService;
        _serviceLimitService = serviceLimitService;
        _clientRoleResolverService = clientRoleResolverService;
        _sectionService = sectionService;
        _membershipService = membershipService;
    }

    public async Task<PagedListViewModel<ChoirMemberListItemViewModel>> GetPagedAsync(
        Guid choirId, PaginateViewModel pagination, CancellationToken ct = default)
    {
        await EnsureManagerDuClientAsync(choirId, ct);

        var query = _context.SpaceMembers
            .AsNoTracking()
            .Include(m => m.User)
            .Where(m => m.ChoirId == choirId
                        && m.Status == MemberStatusEnum.Active
                        && _context.SpaceMemberRoles.Any(
                            r => r.SpaceMemberId == m.Id && r.Role == UserRoleEnum.Manager));

        if (!string.IsNullOrWhiteSpace(pagination.Filter))
            query = query.Where(m =>
                m.User.Firstname.Contains(pagination.Filter) ||
                m.User.Lastname.Contains(pagination.Filter) ||
                m.User.Email != null && m.User.Email.Contains(pagination.Filter));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(m => m.User.Lastname).ThenBy(m => m.User.Firstname).ThenBy(m => m.Id)
            .Skip(pagination.Offset)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        var viewModels = _mapper.Map<List<ChoirMemberListItemViewModel>>(items);
        await AttachRolesAsync(choirId, viewModels, ct);

        return new PagedListViewModel<ChoirMemberListItemViewModel>
        {
            Items = viewModels,
            TotalCount = total,
            CurrentPage = pagination.Page,
            PageSize = pagination.PageSize
        };
    }

    public async Task<ChoirMemberListItemViewModel> AssignAsync(
        Guid choirId, AssignChoirMasterViewModel model, CancellationToken ct = default)
    {
        await EnsureManagerDuClientAsync(choirId, ct);
        await _membershipService.EnsureChoirAcceptsWriteAsync(choirId, ct);

        var user = await _userManager.FindByEmailAsync(model.Email)
            ?? throw new KeyNotFoundException($"Aucun compte pour {model.Email}.");

        // IgnoreQueryFilters : un ancien membre Archived est aussi IsDeleted = true (meme
        // convention partout dans ce service — voir ChoirService.RemoveMemberAsync), donc
        // invisible du filtre de requete par defaut. Sans ceci, la reactivation ci-dessous
        // ne trouverait jamais la ligne existante et en creerait une seconde en doublon.
        var member = await _context.SpaceMembers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.ChoirId == choirId && m.UserId == user.Id, ct);

        // Seule la transition depuis Archived (ou l'absence de ligne) augmente le compte
        // retenu par ServiceLimitService.CountMembersAsync, qui exclut uniquement Archived.
        var wasCounted = member is not null && member.Status != MemberStatusEnum.Archived;
        if (!wasCounted)
            await _serviceLimitService.EnsureCanAddMemberAsync(choirId, ct);

        if (member is null)
        {
            member = new SpaceMember
            {
                Id = ChoraleDbContext.NewIdGuid(),
                ChoirId = choirId,
                SpaceId = choirId,
                UserId = user.Id,
                Status = MemberStatusEnum.Active,
                IsDeleted = false
            };
            _context.SpaceMembers.Add(member);
        }
        else if (member.Status != MemberStatusEnum.Active)
        {
            // Reactive un ancien membre (Archived, Invited, Inactive) plutot que de refuser :
            // designer explicitement quelqu'un comme chef de chœur exprime l'intention
            // qu'il/elle gere cette chorale des maintenant (decision produit, arbitrage
            // Phase 1 point ouvert n°1 — confirme par l'utilisateur en Phase 2).
            member.Status = MemberStatusEnum.Active;
            member.IsDeleted = false;
        }

        var aDejaLeRole = await _context.SpaceMemberRoles
            .AnyAsync(r => r.SpaceMemberId == member.Id && r.Role == UserRoleEnum.Manager, ct);

        if (!aDejaLeRole)
        {
            _context.SpaceMemberRoles.Add(new SpaceMemberRole
            {
                Id = ChoraleDbContext.NewIdGuid(),
                SpaceMemberId = member.Id,
                Role = UserRoleEnum.Manager
            });
            _auditLogService.Record("ChoirMasterAssigned", nameof(SpaceMember), member.Id.ToString(), user.Id);
        }

        await _context.SaveChangesAsync(ct);

        var reloaded = await _context.SpaceMembers
            .AsNoTracking()
            .Include(m => m.User)
            .FirstAsync(m => m.Id == member.Id, ct);

        var viewModel = _mapper.Map<ChoirMemberListItemViewModel>(reloaded);
        await AttachRolesAsync(choirId, [viewModel], ct);
        return viewModel;
    }

    public async Task RevokeAsync(Guid choirId, string userId, CancellationToken ct = default)
    {
        await EnsureManagerDuClientAsync(choirId, ct);
        await _membershipService.EnsureChoirAcceptsWriteAsync(choirId, ct);

        var member = await _context.SpaceMembers
            .FirstOrDefaultAsync(m => m.ChoirId == choirId && m.UserId == userId, ct)
            ?? throw new KeyNotFoundException("Member not found in this choir.");

        var role = await _context.SpaceMemberRoles
            .FirstOrDefaultAsync(r => r.SpaceMemberId == member.Id && r.Role == UserRoleEnum.Manager, ct)
            ?? throw new KeyNotFoundException("Ce membre n'est pas chef de chœur de cette chorale.");

        if (await _sectionService.IsSectionLeaderInChoirAsync(choirId, userId, ct))
            throw new CustomException(
                HttpStatusCode.BadRequest,
                "Retirez d'abord le rôle de chef de pupitre avant de retirer le rôle de chef de chœur.");

        // Point unique de l'invariant "au moins un Manager actif" — voir
        // IMembershipService.EnsureNotLastManagerAsync, partagee avec ChoirMembersService et
        // ChoirService pour ne pas dupliquer ce comptage de securite.
        await _membershipService.EnsureNotLastManagerAsync(choirId, member.Id, ct);

        _context.SpaceMemberRoles.Remove(role);
        _auditLogService.Record("ChoirMasterRevoked", nameof(SpaceMember), member.Id.ToString(), userId);
        await _context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Reservee a l'Admin ou au ResponsableClient du client PROPRIETAIRE de la chorale visee
    /// — resolue depuis la ressource (jamais depuis une valeur declaree par l'appelant),
    /// meme principe que <c>ChoirService.EnsureManagerDuClientAsync</c>. Retourne le
    /// <c>clientId</c> resolu, reutilise par les appelants qui en ont besoin.
    /// </summary>
    private async Task<Guid> EnsureManagerDuClientAsync(Guid choirId, CancellationToken ct)
    {
        var clientId = await _clientRoleResolverService.ResolveChoirClientIdAsync(choirId, ct)
            ?? throw new KeyNotFoundException($"Choir {choirId} not found.");

        if (_currentUserRoles.Contains(UserRoleEnum.Admin)) return clientId;

        var roles = await _clientRoleResolverService.ResolveRolesAsync(
            _currentUserId ?? string.Empty, clientId, ct);

        if (!roles.Contains(UserRoleEnum.ClientManager))
            throw new CustomException(HttpStatusCode.Forbidden, "Vous n'êtes pas responsable de ce client.");

        return clientId;
    }

    /// <summary>
    /// Enrichissement scope a la chorale visee — jamais la requete globale non filtree de
    /// <c>ChoirMembersService.AttachRolesAsync</c> (signalee C9). <c>SectionId</c>/
    /// <c>SectionVoicePart</c> restent a defaut : cet ecran gere des roles, pas des pupitres.
    /// </summary>
    private async Task AttachRolesAsync(
        Guid choirId, List<ChoirMemberListItemViewModel> viewModels, CancellationToken ct)
    {
        if (viewModels.Count == 0) return;

        var sectionLeaderUserIds = await _context.Sections
            .AsNoTracking()
            .Where(p => p.ChoirId == choirId && p.SectionLeaderId != null)
            .Select(p => p.SectionLeaderId!)
            .ToListAsync(ct);

        foreach (var viewModel in viewModels)
        {
            var roles = new List<string> { nameof(UserRoleEnum.Singer), nameof(UserRoleEnum.Manager) };
            if (sectionLeaderUserIds.Contains(viewModel.UserId))
                roles.Add(nameof(UserRoleEnum.SectionLeader));

            viewModel.Roles = roles;
        }
    }
}
