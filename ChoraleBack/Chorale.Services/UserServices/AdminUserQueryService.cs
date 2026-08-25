using System.Linq.Expressions;
using System.Net;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Common.Helpers;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.AdminUsers;
using Microsoft.EntityFrameworkCore;

namespace ChoraleBackEnd.Services.UserServices;

public interface IAdminUserQueryService
{
    Task<PagedListViewModel<AdminUserListItemViewModel>> GetPagedAsync(AdminUsersPagedFilterViewModel filter, CancellationToken ct = default);
    Task<PagedListViewModel<AdminChoirUserListItemViewModel>> GetChoirUsersPagedAsync(AdminChoirUsersPagedFilterViewModel filter, CancellationToken ct = default);
    Task<PagedListViewModel<AdminEventUserListItemViewModel>> GetEventUsersPagedAsync(AdminEventUsersPagedFilterViewModel filter, CancellationToken ct = default);
    Task<PagedListViewModel<AdminUnattachedUserListItemViewModel>> GetUnattachedUsersPagedAsync(AdminUsersPagedFilterViewModel filter, CancellationToken ct = default);
    Task<AdminUserDetailViewModel> GetUserDetailAsync(string userId, CancellationToken ct = default);
}

/// <summary>
/// Lectures de l'administration generale des comptes : les quatre listings de l'ecran
/// Utilisateurs (administrateurs, rattachements chorale, rattachements evenement, comptes sans
/// rattachement) et la fiche detaillee.
/// </summary>
/// <remarks>
/// Separe d'<see cref="IAdminUserService"/>, qui porte le cycle de vie du compte (creation,
/// identite, activation, mot de passe, invitation, suppression). La frontiere est l'axe
/// lecture/ecriture : ce service ne modifie rien, l'autre s'appuie sur lui pour renvoyer la
/// fiche a jour apres ecriture. La dependance est a sens unique — ne pas l'inverser.
///
/// Une ligne de listing = un RATTACHEMENT, jamais une personne : la meme personne apparait
/// autant de fois qu'elle a de rattachements. Les actions vivent sur la fiche, pas sur la ligne.
/// </remarks>
public sealed class AdminUserQueryService : BaseService, IAdminUserQueryService
{
    // Revalidee ici en plus de [MaxLength(200)] sur les ViewModels : un appel direct au
    // service (tests, futur appelant interne) ne doit pas pouvoir contourner la borne.
    // Meme regle que ClientService.MaxClientIds.
    private const int MaxChoirIds = 200;
    private const int MaxEventIds = 200;

    private readonly ISectionVoicePartLookupService _sectionVoicePartLookupService;

    public AdminUserQueryService(
        IServiceProvider serviceProvider, ISectionVoicePartLookupService sectionVoicePartLookupService)
        : base(serviceProvider)
    {
        _sectionVoicePartLookupService = sectionVoicePartLookupService;
    }


    // Listes blanches de tri : chaque cle est un nom de colonne explicite transmis par le
    // front (`SortActive`), jamais interprete comme un nom de propriete — voir TriHelper.
    private static readonly IReadOnlyDictionary<string, Expression<Func<User, object?>>> AdminsSortableColumns =
        new Dictionary<string, Expression<Func<User, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Lastname"] = u => u.Lastname,
            ["Firstname"] = u => u.Firstname,
            ["Email"] = u => u.Email,
            ["IsActive"] = u => u.IsActive,
            ["LastConnection"] = u => u.LastConnection,
            ["CreatedAt"] = u => u.CreatedAt
        };

    private static readonly IReadOnlyDictionary<string, Expression<Func<SpaceMember, object?>>> ChoirMembersSortableColumns =
        new Dictionary<string, Expression<Func<SpaceMember, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Lastname"] = m => m.User.Lastname,
            ["Firstname"] = m => m.User.Firstname,
            ["Email"] = m => m.User.Email,
            ["ChoirName"] = m => m.Choir!.Name,
            ["Status"] = m => m.Status,
            ["IsActive"] = m => m.User.IsActive,
            ["LastActive"] = m => m.User.LastActive
        };

    private static readonly IReadOnlyDictionary<string, Expression<Func<SpaceMember, object?>>> EventMembersSortableColumns =
        new Dictionary<string, Expression<Func<SpaceMember, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Lastname"] = m => m.User.Lastname,
            ["Firstname"] = m => m.User.Firstname,
            ["Email"] = m => m.User.Email,
            ["Status"] = m => m.Status
        };

    private static readonly IReadOnlyDictionary<string, Expression<Func<User, object?>>> UnattachedSortableColumns =
        new Dictionary<string, Expression<Func<User, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Lastname"] = u => u.Lastname,
            ["Firstname"] = u => u.Firstname,
            ["Email"] = u => u.Email,
            ["IsActive"] = u => u.IsActive,
            ["IsGuestAccount"] = u => u.IsGuestAccount,
            ["CreatedAt"] = u => u.CreatedAt,
            ["LastConnection"] = u => u.LastConnection
        };

    public async Task<PagedListViewModel<AdminUserListItemViewModel>> GetPagedAsync(
        AdminUsersPagedFilterViewModel pagination, CancellationToken ct = default)
    {
        var query = _userManager.Users
            .Where(u => !u.IsDeleted)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(pagination.Filter))
            query = query.Where(u =>
                u.Firstname.Contains(pagination.Filter) ||
                u.Lastname.Contains(pagination.Filter) ||
                u.Email != null && u.Email.Contains(pagination.Filter));

        if (pagination.IsActive.HasValue)
            query = query.Where(u => u.IsActive == pagination.IsActive.Value);

        if (pagination.IsGuestAccount.HasValue)
            query = query.Where(u => u.IsGuestAccount == pagination.IsGuestAccount.Value);

        var adminIds = await (
            from u in query
            join ur in _context.UserRoles on u.Id equals ur.UserId
            join r in _context.Roles on ur.RoleId equals r.Id
            where r.Name == UserRoleEnum.Admin.ToString()
            select u.Id
        ).ToListAsync(ct);

        var admins = query.Where(u => adminIds.Contains(u.Id));

        var total = await admins.CountAsync(ct);
        var items = await admins
            .ApplySort(
                pagination.SortActive, pagination.SortDirection, AdminsSortableColumns, u => u.Id,
                q => q.OrderBy(u => u.Lastname).ThenBy(u => u.Firstname).ThenBy(u => u.Id))
            .Skip(pagination.Offset)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        var viewModels = _mapper.Map<List<AdminUserListItemViewModel>>(items);
        await AttachCreatedByNameAsync(viewModels, ct);

        return new PagedListViewModel<AdminUserListItemViewModel>
        {
            Items = viewModels,
            TotalCount = total,
            CurrentPage = pagination.Page,
            PageSize = pagination.PageSize
        };
    }

    public async Task<PagedListViewModel<AdminChoirUserListItemViewModel>> GetChoirUsersPagedAsync(
        AdminChoirUsersPagedFilterViewModel filter, CancellationToken ct = default)
    {
        var query = ChoirMembershipsQuery();

        // Liste presente mais vide : zero result, jamais un repli sur la liste complete.
        // Liste absente (null) : filtre inactif. Meme regle que ClientService.GetPagedAsync.
        if (filter.ChoirIds is { } choirIds)
        {
            if (choirIds.Count > MaxChoirIds)
                throw new CustomException(HttpStatusCode.BadRequest,
                    $"Trop d'identifiants transmis : {choirIds.Count} sur un maximum de {MaxChoirIds}.");

            query = query.Where(m => m.ChoirId != null && choirIds.Contains(m.ChoirId.Value));
        }

        if (filter.Status is { } status)
            query = query.Where(m => m.Status == status);

        if (filter.IsActive is { } isActive)
            query = query.Where(m => m.User.IsActive == isActive);

        if (!string.IsNullOrWhiteSpace(filter.Filter))
            query = query.Where(m =>
                m.User.Firstname.Contains(filter.Filter) ||
                m.User.Lastname.Contains(filter.Filter) ||
                m.User.Email != null && m.User.Email.Contains(filter.Filter) ||
                m.Choir!.Name.Contains(filter.Filter));

        if (filter.Role is { } role)
            query = query.Where(m =>
                _context.SpaceMemberRoles.Any(r => r.SpaceMemberId == m.Id && r.Role == role)
                || role == UserRoleEnum.SectionLeader
                    && _context.Sections.Any(p => p.ChoirId == m.ChoirId && p.SectionLeaderId == m.UserId));

        if (filter.VoicePart is { } voicePart)
            query = query.Where(m => _context.SectionMembers.Any(mp =>
                mp.UserId == m.UserId && mp.Section.ChoirId == m.ChoirId && mp.Section.VoicePart == voicePart));

        var total = await query.CountAsync(ct);
        var items = await query
            .ApplySort(
                filter.SortActive, filter.SortDirection, ChoirMembersSortableColumns, m => m.Id,
                q => q.OrderBy(m => m.User.Lastname).ThenBy(m => m.User.Firstname).ThenBy(m => m.Id))
            .Skip(filter.Offset)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        var viewModels = _mapper.Map<List<AdminChoirUserListItemViewModel>>(items);
        await AttachRolesAndVoicePartAsync(viewModels, ct);

        return new PagedListViewModel<AdminChoirUserListItemViewModel>
        {
            Items = viewModels,
            TotalCount = total,
            CurrentPage = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<PagedListViewModel<AdminEventUserListItemViewModel>> GetEventUsersPagedAsync(
        AdminEventUsersPagedFilterViewModel filter, CancellationToken ct = default)
    {
        var query = EventMembershipsQuery();

        // Liste presente mais vide : zero result, jamais un repli sur la liste complete.
        // Liste absente (null) : filtre inactif. Meme regle que ClientService.GetPagedAsync.
        if (filter.EventIds is { } eventIds)
        {
            if (eventIds.Count > MaxEventIds)
                throw new CustomException(HttpStatusCode.BadRequest,
                    $"Trop d'identifiants transmis : {eventIds.Count} sur un maximum de {MaxEventIds}.");

            query = query.Where(m => eventIds.Contains(m.SpaceId));
        }

        if (filter.Presence is { } presence)
            query = query.Where(m => m.Presence == presence);

        if (filter.Role is { } role)
            query = query.Where(m => _context.SpaceMemberRoles.Any(r => r.SpaceMemberId == m.Id && r.Role == role));

        if (!string.IsNullOrWhiteSpace(filter.Filter))
            query = query.Where(m =>
                m.User.Firstname.Contains(filter.Filter) ||
                m.User.Lastname.Contains(filter.Filter) ||
                m.User.Email != null && m.User.Email.Contains(filter.Filter));

        if (filter.Upcoming is { } aVenir)
        {
            var maintenant = DateTime.UtcNow;
            query = aVenir
                ? query.Where(m => _context.Events.Any(e => e.Id == m.SpaceId && e.StartDate >= maintenant))
                : query.Where(m => _context.Events.Any(e => e.Id == m.SpaceId && e.StartDate < maintenant));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .ApplySort(
                filter.SortActive, filter.SortDirection, EventMembersSortableColumns, m => m.Id,
                q => q.OrderBy(m => m.User.Lastname).ThenBy(m => m.User.Firstname).ThenBy(m => m.Id))
            .Skip(filter.Offset)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        var viewModels = _mapper.Map<List<AdminEventUserListItemViewModel>>(items);
        await AttachEventDetailsAsync(viewModels, ct);

        return new PagedListViewModel<AdminEventUserListItemViewModel>
        {
            Items = viewModels,
            TotalCount = total,
            CurrentPage = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<PagedListViewModel<AdminUnattachedUserListItemViewModel>> GetUnattachedUsersPagedAsync(
        AdminUsersPagedFilterViewModel pagination, CancellationToken ct = default)
    {
        var attachedUserIds = await _context.SpaceMembers
            .Where(m =>
                m.SpaceId == m.ChoirId && _context.Choirs.Any(c => c.Id == m.ChoirId)
                || m.SpaceId != m.ChoirId && _context.Events.Any(e => e.Id == m.SpaceId))
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync(ct);

        var adminUserIds = await _context.UserRoles
            .Where(ur => _context.Roles.Any(r => r.Id == ur.RoleId && r.Name == UserRoleEnum.Admin.ToString()))
            .Select(ur => ur.UserId)
            .Distinct()
            .ToListAsync(ct);

        var query = _context.Users
            .AsNoTracking()
            .Where(u => !u.IsDeleted)
            .Where(u => !attachedUserIds.Contains(u.Id))
            .Where(u => !adminUserIds.Contains(u.Id));

        if (!string.IsNullOrWhiteSpace(pagination.Filter))
            query = query.Where(u =>
                u.Firstname.Contains(pagination.Filter) ||
                u.Lastname.Contains(pagination.Filter) ||
                u.Email != null && u.Email.Contains(pagination.Filter));

        if (pagination.IsActive.HasValue)
            query = query.Where(u => u.IsActive == pagination.IsActive.Value);

        if (pagination.IsGuestAccount.HasValue)
            query = query.Where(u => u.IsGuestAccount == pagination.IsGuestAccount.Value);

        var total = await query.CountAsync(ct);
        var items = await query
            .ApplySort(
                pagination.SortActive, pagination.SortDirection, UnattachedSortableColumns, u => u.Id,
                q => q.OrderBy(u => u.Lastname).ThenBy(u => u.Firstname).ThenBy(u => u.Id))
            .Skip(pagination.Offset)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        var viewModels = _mapper.Map<List<AdminUnattachedUserListItemViewModel>>(items);
        await AttachClientAssignmentAsync(viewModels, ct);

        return new PagedListViewModel<AdminUnattachedUserListItemViewModel>
        {
            Items = viewModels,
            TotalCount = total,
            CurrentPage = pagination.Page,
            PageSize = pagination.PageSize
        };
    }

    public async Task<AdminUserDetailViewModel> GetUserDetailAsync(string userId, CancellationToken ct = default)
    {
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new KeyNotFoundException($"User {userId} not found.");

        var viewModel = _mapper.Map<AdminUserDetailViewModel>(user);

        var choirs = await ChoirMembershipsQuery()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.Choir!.Name)
            .ThenBy(m => m.Id)
            .ToListAsync(ct);
        viewModel.Choirs = _mapper.Map<List<AdminChoirUserListItemViewModel>>(choirs);
        await AttachRolesAndVoicePartAsync(viewModel.Choirs, ct);

        var events = await EventMembershipsQuery()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.Id)
            .ToListAsync(ct);
        viewModel.Events = _mapper.Map<List<AdminEventUserListItemViewModel>>(events);
        await AttachEventDetailsAsync(viewModel.Events, ct);

        var membersClients = await _context.ClientMembers
            .AsNoTracking()
            .Include(m => m.Client)
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.Id)
            .ToListAsync(ct);
        viewModel.ClientAttachments = _mapper.Map<List<AdminUserDetailClientItemViewModel>>(membersClients);

        return viewModel;
    }

    private IQueryable<SpaceMember> ChoirMembershipsQuery()
        => _context.SpaceMembers
            .AsNoTracking()
            .Include(m => m.User)
            .Include(m => m.Choir)
            .Where(m => m.SpaceId == m.ChoirId)
            .Where(m => _context.Choirs.Any(c => c.Id == m.ChoirId))
            .Where(m => _context.Users.Any(u => u.Id == m.UserId));

    private IQueryable<SpaceMember> EventMembershipsQuery()
        => _context.SpaceMembers
            .AsNoTracking()
            .Include(m => m.User)
            .Where(m => m.SpaceId != m.ChoirId)
            .Where(m => _context.Events.Any(e => e.Id == m.SpaceId))
            .Where(m => _context.Users.Any(u => u.Id == m.UserId));

    private async Task AttachCreatedByNameAsync(List<AdminUserListItemViewModel> viewModels, CancellationToken ct)
    {
        if (viewModels.Count == 0) return;

        var creatorIds = viewModels
            .Where(v => v.CreatedByUserId is not null)
            .Select(v => v.CreatedByUserId!)
            .Distinct()
            .ToList();

        if (creatorIds.Count == 0) return;

        var creators = await _context.Users
            .IgnoreQueryFilters()
            .Where(u => creatorIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Firstname, u.Lastname })
            .ToListAsync(ct);

        var creatorsLookup = creators.ToDictionary(c => c.Id, c => $"{c.Firstname} {c.Lastname}".Trim());

        foreach (var viewModel in viewModels)
        {
            if (viewModel.CreatedByUserId is not null && creatorsLookup.TryGetValue(viewModel.CreatedByUserId, out var name))
                viewModel.CreatedByName = name;
        }
    }

    private async Task AttachRolesAndVoicePartAsync(List<AdminChoirUserListItemViewModel> viewModels, CancellationToken ct)
    {
        if (viewModels.Count == 0) return;

        var ids = viewModels.Select(v => v.Id).ToList();
        var userIds = viewModels.Select(v => v.UserId).Distinct().ToList();
        var choirIds = viewModels.Select(v => v.ChoirId).Distinct().ToList();

        var rolesById = (await _context.SpaceMemberRoles
                .AsNoTracking()
                .Where(r => ids.Contains(r.SpaceMemberId))
                .ToListAsync(ct))
            .GroupBy(r => r.SpaceMemberId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Role.ToString()).ToList());

        var chefsSection = await _context.Sections
            .AsNoTracking()
            .Where(p => p.SectionLeaderId != null && choirIds.Contains(p.ChoirId))
            .Select(p => new { p.ChoirId, p.SectionLeaderId })
            .ToListAsync(ct);

        var sectionByUserAndChoir = await _sectionVoicePartLookupService
            .GetPrimarySectionsAsync(userIds, choirIds, ct);

        foreach (var viewModel in viewModels)
        {
            var roles = new List<string>();

            if (rolesById.TryGetValue(viewModel.Id, out var explicitRoles))
                roles.AddRange(explicitRoles);

            if (chefsSection.Any(p => p.ChoirId == viewModel.ChoirId && p.SectionLeaderId == viewModel.UserId))
                roles.Add(UserRoleEnum.SectionLeader.ToString());

            viewModel.Roles = roles.Count > 0
                ? roles.Distinct().ToList()
                : [UserRoleEnum.Singer.ToString()];

            if (sectionByUserAndChoir.TryGetValue((viewModel.UserId, viewModel.ChoirId), out var section))
                viewModel.PrimaryVoicePart = section.VoicePart;
        }
    }

    private async Task AttachEventDetailsAsync(List<AdminEventUserListItemViewModel> viewModels, CancellationToken ct)
    {
        if (viewModels.Count == 0) return;

        var ids = viewModels.Select(v => v.Id).ToList();
        var eventIds = viewModels.Select(v => v.EventId).Distinct().ToList();

        var events = await _context.Events
            .AsNoTracking()
            .Where(e => eventIds.Contains(e.Id))
            .Select(e => new { e.Id, e.Title, e.StartDate, e.ChoirId })
            .ToListAsync(ct);

        var eventsLookup = events.ToDictionary(e => e.Id, e => e);

        var choirIds = events
            .Where(e => e.ChoirId is not null)
            .Select(e => e.ChoirId!.Value)
            .Distinct()
            .ToList();

        Dictionary<Guid, string> choirsLookup = [];
        if (choirIds.Count > 0)
            choirsLookup = await _context.Choirs
                .AsNoTracking()
                .Where(c => choirIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Name })
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var rolesById = (await _context.SpaceMemberRoles
                .AsNoTracking()
                .Where(r => ids.Contains(r.SpaceMemberId))
                .ToListAsync(ct))
            .GroupBy(r => r.SpaceMemberId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Role).ToList());

        foreach (var viewModel in viewModels)
        {
            if (!eventsLookup.TryGetValue(viewModel.EventId, out var evt)) continue;

            viewModel.EventTitle = evt.Title;
            viewModel.EventStartDate = evt.StartDate;
            viewModel.ChoirId = evt.ChoirId;
            viewModel.ChoirName = evt.ChoirId is { } choirId
                ? choirsLookup.GetValueOrDefault(choirId)
                : null;

            var roles = rolesById.GetValueOrDefault(viewModel.Id, []);
            viewModel.Role = roles.Contains(UserRoleEnum.Organizer)
                ? UserRoleEnum.Organizer.ToString()
                : UserRoleEnum.Participant.ToString();
        }
    }

    private async Task AttachClientAssignmentAsync(List<AdminUnattachedUserListItemViewModel> viewModels, CancellationToken ct)
    {
        if (viewModels.Count == 0) return;

        var userIds = viewModels.Select(v => v.Id).ToList();

        var membersClients = await _context.ClientMembers
            .AsNoTracking()
            .Where(m => userIds.Contains(m.UserId))
            .Select(m => new { m.UserId, m.Role, ClientName = m.Client.Name })
            .ToListAsync(ct);

        var lookup = membersClients
            .GroupBy(m => m.UserId)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var viewModel in viewModels)
        {
            if (!lookup.TryGetValue(viewModel.Id, out var member)) continue;

            viewModel.ClientName = member.ClientName;
            viewModel.ClientRole = member.Role.ToString();
        }
    }
}
