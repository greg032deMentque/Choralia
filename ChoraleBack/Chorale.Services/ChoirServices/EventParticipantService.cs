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
using ChoraleBackEnd.ViewModels.Events;
using Microsoft.EntityFrameworkCore;

namespace ChoraleBackEnd.Services.ChoirServices;

public interface IEventParticipantService
{
    Task<EventParticipantListItemViewModel> InviteAsync(InviteEventParticipantViewModel model, CancellationToken ct = default);
    Task<EventParticipantListItemViewModel> RsvpAsync(EventRsvpViewModel model, CancellationToken ct = default);
    Task<PagedListViewModel<EventParticipantListItemViewModel>> GetPagedAsync(EventParticipantsPagedFilterViewModel filter, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class EventParticipantService : BaseService, IEventParticipantService
{
    // Liste blanche de tri : sans elle, Skip/Take sur une requete non triee n'est pas
    // reproductible (deux pages consecutives peuvent se recouvrir ou perdre des lignes) — voir
    // TriHelper. Tri par defaut par Lastname/Firstname, departage sur Id.
    private static readonly IReadOnlyDictionary<string, Expression<Func<SpaceMember, object?>>> ParticipantsSortableColumns =
        new Dictionary<string, Expression<Func<SpaceMember, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Lastname"] = m => m.User.Lastname,
            ["Firstname"] = m => m.User.Firstname,
            ["Email"] = m => m.User.Email,
            ["Attendance"] = m => m.Presence
        };

    private readonly IEventAuthorizationService _authorizationService;
    private readonly IUserInvitationService _userInvitationService;
    private readonly IServiceLimitService _serviceLimitService;
    private readonly IMembershipService _membershipService;
    private readonly IAuditLogService _auditLogService;

    public EventParticipantService(
        IServiceProvider serviceProvider,
        IEventAuthorizationService authorizationService,
        IUserInvitationService userInvitationService,
        IServiceLimitService serviceLimitService,
        IMembershipService membershipService,
        IAuditLogService auditLogService)
        : base(serviceProvider)
    {
        _authorizationService = authorizationService;
        _userInvitationService = userInvitationService;
        _serviceLimitService = serviceLimitService;
        _membershipService = membershipService;
        _auditLogService = auditLogService;
    }

    public async Task<EventParticipantListItemViewModel> InviteAsync(
        InviteEventParticipantViewModel model, CancellationToken ct = default)
    {
        var evt = await _context.Events
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == model.EventId, ct)
            ?? throw new KeyNotFoundException($"Event {model.EventId} not found.");

        await _authorizationService.EnsureEventManagerAsync(evt, ct);

        if (EventStateHelper.IsFinished(evt.StartDate, evt.EndDate))
            throw new CustomException(
                HttpStatusCode.BadRequest,
                "Impossible d'inviter : l'événement est terminé.");

        await EnsureWriteChoirAsync(evt.ChoirId, ct);

        // Verifie le plafond AVANT de create quoi que ce soit : un compte invite ou un
        // SpaceMember crees puis abandonnes en cas de refus seraient une entite partielle.
        await _serviceLimitService.EnsureCanAddMemberAsync(evt.Id, ct);

        var user = await _userInvitationService.InviteGuestAsync(
            model.Email, model.Firstname, SpaceTypeEnum.Event, evt.Title, ct: ct);

        var alreadyParticipant = await _context.SpaceMembers
            .AnyAsync(m => m.UserId == user.Id && m.SpaceId == evt.Id, ct);

        if (alreadyParticipant)
            throw new CustomException(
                "Utilisateur déjà participant de cet événement.",
                "Ce membre participe déjà à cet événement.",
                HttpStatusCode.Conflict);

        // Regle unique (`04` § Membre) : « un membre actif d'une chorale est participant des
        // evenements publies a venir de cette chorale ». Un invite deja membre Actif de la
        // chorale porteuse entre donc directement Actif, sans repasser par Invite — les deux
        // autres cas (invite externe, membre non-actif, evenement autonome) gardent le
        // comportement existant.
        var isActiveChoirMember = evt.ChoirId.HasValue && await _context.SpaceMembers
            .AsNoTracking()
            .AnyAsync(m => m.UserId == user.Id && m.ChoirId == evt.ChoirId
                           && m.SpaceId == evt.ChoirId && m.Status == MemberStatusEnum.Active, ct);

        var member = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = user.Id,
            SpaceId = evt.Id,
            ChoirId = evt.ChoirId,
            // Invite, pas Active : un participant n'entre en acces qu'a sa premiere connexion —
            // voir AccountService.PromoteInvitedMembershipsAsync. Presence reste nulle :
            // c'est au participant de repondre (RSVP), pas a l'invitation de le decider pour lui.
            Status = isActiveChoirMember ? MemberStatusEnum.Active : MemberStatusEnum.Invited,
            Presence = isActiveChoirMember ? AttendanceEnum.NoReply : null,
            IsDeleted = false
        };
        _context.SpaceMembers.Add(member);

        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SpaceMemberId = member.Id,
            Role = UserRoleEnum.Participant
        });

        _auditLogService.Record(
            "EventParticipantInvited",
            nameof(SpaceMember),
            member.Id.ToString(),
            $"Participant invité sur l'événement {evt.Id} ({evt.Title}).");

        await _context.SaveChangesAsync(ct);
        return await BuildViewModelAsync(member.Id, ct);
    }

    /// <summary>
    /// Ferme l'invitation sur un evenement DEJA rattache a une chorale, des lors que cette
    /// chorale n'accepte plus le contenu — voir <see cref="IMembershipService.EnsureCanWriteAsync"/>.
    /// Un evenement autonome (sans chorale) n'est pas concerne.
    /// </summary>
    private async Task EnsureWriteChoirAsync(Guid? choirId, CancellationToken ct)
    {
        if (choirId.HasValue)
            await _membershipService.EnsureCanWriteAsync(choirId.Value, ct);
    }

    public async Task<EventParticipantListItemViewModel> RsvpAsync(
        EventRsvpViewModel model, CancellationToken ct = default)
    {
        var evt = await _context.Events
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == model.EventId, ct)
            ?? throw new KeyNotFoundException($"Event {model.EventId} not found.");

        var member = await _context.SpaceMembers
            .FirstOrDefaultAsync(m => m.SpaceId == evt.Id && m.UserId == _currentUserId, ct)
            ?? throw new CustomException(HttpStatusCode.Forbidden, "Vous ne participez pas à cet événement.");

        if (EventStateHelper.IsFinished(evt.StartDate, evt.EndDate))
            throw new CustomException(
                HttpStatusCode.BadRequest,
                "Impossible de modifier sa réponse : l'événement est terminé.");

        member.Presence = model.Presence;
        await _context.SaveChangesAsync(ct);
        return await BuildViewModelAsync(member.Id, ct);
    }

    public async Task<PagedListViewModel<EventParticipantListItemViewModel>> GetPagedAsync(
        EventParticipantsPagedFilterViewModel filter, CancellationToken ct = default)
    {
        var isAllowed = _authorizationService.IsAdmin()
            || await _authorizationService.IsSpaceMemberAsync(filter.EventId, ct);

        if (!isAllowed)
            throw new CustomException(HttpStatusCode.Forbidden, "Accès réservé aux membres de cet événement.");

        var query = _context.SpaceMembers
            .AsNoTracking()
            .Include(m => m.User)
            .Where(m => m.SpaceId == filter.EventId);

        if (!string.IsNullOrWhiteSpace(filter.Filter))
            query = query.Where(m =>
                m.User.Firstname.Contains(filter.Filter) ||
                m.User.Lastname.Contains(filter.Filter) ||
                m.User.Email != null && m.User.Email.Contains(filter.Filter));

        var total = await query.CountAsync(ct);
        var items = await query
            .ApplySort(
                filter.SortActive, filter.SortDirection, ParticipantsSortableColumns, m => m.Id,
                q => q.OrderBy(m => m.User.Lastname).ThenBy(m => m.User.Firstname).ThenBy(m => m.Id))
            .Skip(filter.Offset)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        var viewModels = _mapper.Map<List<EventParticipantListItemViewModel>>(items);
        await AttachRolesAsync(viewModels, ct);

        return new PagedListViewModel<EventParticipantListItemViewModel>
        {
            Items = viewModels,
            TotalCount = total,
            CurrentPage = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var member = await _context.SpaceMembers
            .FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new KeyNotFoundException($"Participant {id} not found.");

        var evt = await _context.Events
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == member.SpaceId, ct)
            ?? throw new KeyNotFoundException($"Event {member.SpaceId} not found.");

        await _authorizationService.EnsureEventManagerAsync(evt, ct);

        member.IsDeleted = true;
        member.Status = MemberStatusEnum.Archived;
        await _context.SaveChangesAsync(ct);
    }

    private async Task<EventParticipantListItemViewModel> BuildViewModelAsync(Guid spaceMemberId, CancellationToken ct)
    {
        var member = await _context.SpaceMembers
            .AsNoTracking()
            .Include(m => m.User)
            .FirstAsync(m => m.Id == spaceMemberId, ct);

        var viewModel = _mapper.Map<EventParticipantListItemViewModel>(member);
        await AttachRolesAsync([viewModel], ct);
        return viewModel;
    }

    private async Task AttachRolesAsync(List<EventParticipantListItemViewModel> viewModels, CancellationToken ct)
    {
        if (viewModels.Count == 0) return;

        var ids = viewModels.Select(v => v.Id).ToList();
        var roles = await _context.SpaceMemberRoles
            .AsNoTracking()
            .Where(r => ids.Contains(r.SpaceMemberId))
            .ToListAsync(ct);

        var rolesLookup = roles
            .GroupBy(r => r.SpaceMemberId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Role.ToString()).ToList());

        foreach (var viewModel in viewModels)
            viewModel.Roles = rolesLookup.GetValueOrDefault(viewModel.Id, []);
    }
}
