using System.Linq.Expressions;
using System.Net;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Helpers;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.Onboarding;
using Microsoft.EntityFrameworkCore;

namespace ChoraleBackEnd.Services.OnboardingServices;

/// <summary>
/// Demandes d'adhesion deposees via un code de rattachement (lot 6). Le canal ne decide pas
/// de l'admission, il decide seulement si elle est deja prise — c'est le Responsable de
/// l'espace qui admet ou refuse (matrice `02` : ni Organizer ni SectionLeader).
/// </summary>
public interface IMembershipRequestService
{
    Task<MyRequestViewModel> RequestMembershipAsync(RequestMembershipViewModel model, CancellationToken ct = default);
    Task<PagedListViewModel<MyRequestViewModel>> MyRequestsAsync(PaginateViewModel pagination, CancellationToken ct = default);
    Task CancelAsync(Guid id, CancellationToken ct = default);
    Task<PagedListViewModel<MembershipRequestListItemViewModel>> GetPagedAsync(
        Guid spaceId, PaginateViewModel pagination, CancellationToken ct = default);
    Task<MembershipRequestListItemViewModel> ApproveAsync(
        Guid spaceId, Guid id, ApproveRequestViewModel model, CancellationToken ct = default);
    Task<MembershipRequestListItemViewModel> DeclineAsync(
        Guid spaceId, Guid id, DeclineRequestViewModel model, CancellationToken ct = default);
}

public sealed class MembershipRequestService : BaseService, IMembershipRequestService
{
    private const int DeclineDelayDays = 30;
    private const string MessageRequestBlocked =
        "Impossible d'enregistrer votre demande pour le moment.";

    private static readonly IReadOnlyDictionary<string, Expression<Func<MembershipRequest, object?>>> SortableColumns =
        new Dictionary<string, Expression<Func<MembershipRequest, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["CreatedAt"] = d => d.CreatedAt,
            ["Status"] = d => d.Status
        };

    private readonly IJoinCodeService _joinCodeService;
    private readonly ISpaceRoleResolverService _spaceRoleResolverService;
    private readonly IServiceLimitService _serviceLimitService;
    private readonly IMemberEnrollmentService _memberEnrollmentService;

    public MembershipRequestService(
        IServiceProvider serviceProvider,
        IJoinCodeService joinCodeService,
        ISpaceRoleResolverService spaceRoleResolverService,
        IServiceLimitService serviceLimitService,
        IMemberEnrollmentService memberEnrollmentService)
        : base(serviceProvider)
    {
        _memberEnrollmentService = memberEnrollmentService;
        _joinCodeService = joinCodeService;
        _spaceRoleResolverService = spaceRoleResolverService;
        _serviceLimitService = serviceLimitService;
    }

    public async Task<MyRequestViewModel> RequestMembershipAsync(
        RequestMembershipViewModel model, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_currentUserId))
            throw new CustomException(HttpStatusCode.Unauthorized, "Non authentifié.");

        var space = await _joinCodeService.ResolveActiveSpaceByCodeAsync(model.Code, ct);

        await EnsureNoBlockerAsync(space.Id, ct);

        // Une demande d'un compte non verifie est enregistree, mais ne sera jamais servie au
        // Responsable (voir le filtre de GetPagedAsync) — aucun controle EmailConfirmed ici.
        var request = new MembershipRequest
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SpaceId = space.Id,
            UserId = _currentUserId,
            Status = MembershipRequestStatusEnum.Pending,
            Message = model.Message,
            IsDeleted = false
        };

        _context.MembershipRequests.Add(request);
        await _context.SaveChangesAsync(ct);

        return await MapWithSpaceNameAsync(request, ct);
    }

    public async Task<PagedListViewModel<MyRequestViewModel>> MyRequestsAsync(
        PaginateViewModel pagination, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_currentUserId))
            throw new CustomException(HttpStatusCode.Unauthorized, "Non authentifié.");

        var query = _context.MembershipRequests
            .AsNoTracking()
            .Where(d => d.UserId == _currentUserId && d.Status == MembershipRequestStatusEnum.Pending);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(d => d.CreatedAt)
            .ThenBy(d => d.Id)
            .Skip(pagination.Offset)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        // Les noms de chorale sont charges en UNE requete pour toute la page : la boucle
        // appelait auparavant une requete par element (N+1 borne par la taille de page).
        var spaceIds = items.Select(d => d.SpaceId).Distinct().ToList();
        var spaceNames = await _context.Choirs
            .AsNoTracking()
            .Where(c => spaceIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var viewModels = items
            .Select(request =>
            {
                var viewModel = _mapper.Map<MyRequestViewModel>(request);
                viewModel.SpaceName = spaceNames.GetValueOrDefault(request.SpaceId, string.Empty);
                return viewModel;
            })
            .ToList();

        return new PagedListViewModel<MyRequestViewModel>
        {
            Items = viewModels,
            TotalCount = total,
            CurrentPage = pagination.Page,
            PageSize = pagination.PageSize
        };
    }

    public async Task CancelAsync(Guid id, CancellationToken ct = default)
    {
        var request = await _context.MembershipRequests
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == _currentUserId
                && d.Status == MembershipRequestStatusEnum.Pending, ct)
            ?? throw new KeyNotFoundException($"Request {id} not found.");

        request.Status = MembershipRequestStatusEnum.Cancelled;
        request.HandledByUserId = _currentUserId;
        request.HandledAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
    }

    public async Task<PagedListViewModel<MembershipRequestListItemViewModel>> GetPagedAsync(
        Guid spaceId, PaginateViewModel pagination, CancellationToken ct = default)
    {
        await EnsureManagerSpaceAsync(spaceId, ct);

        // Une demande d'un compte non verifie n'est JAMAIS servie au Responsable (decision
        // produit) : elle existe en base pour la relation, mais reste invisible tant que
        // l'email n'est pas confirme.
        var query = _context.MembershipRequests
            .AsNoTracking()
            .Include(d => d.User)
            .Where(d => d.SpaceId == spaceId
                && d.Status == MembershipRequestStatusEnum.Pending
                && d.User.EmailConfirmed);

        var total = await query.CountAsync(ct);
        var items = await query
            .ApplySort(
                pagination.SortActive, pagination.SortDirection, SortableColumns, d => d.Id,
                q => q.OrderBy(d => d.CreatedAt).ThenBy(d => d.Id))
            .Skip(pagination.Offset)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        return new PagedListViewModel<MembershipRequestListItemViewModel>
        {
            Items = _mapper.Map<List<MembershipRequestListItemViewModel>>(items),
            TotalCount = total,
            CurrentPage = pagination.Page,
            PageSize = pagination.PageSize
        };
    }

    public async Task<MembershipRequestListItemViewModel> ApproveAsync(
        Guid spaceId, Guid id, ApproveRequestViewModel model, CancellationToken ct = default)
    {
        await EnsureManagerSpaceAsync(spaceId, ct);

        var request = await _context.MembershipRequests
            .Include(d => d.User)
            .FirstOrDefaultAsync(d => d.Id == id && d.SpaceId == spaceId
                && d.Status == MembershipRequestStatusEnum.Pending, ct)
            ?? throw new KeyNotFoundException($"Request {id} not found.");

        // Admission = affectation (`04`) : voix et role exiges dans la meme operation, jamais
        // une admission "nue".
        if (model.PrimaryVoicePart is not { } voicePart || model.Role is not { } role)
            throw new CustomException(HttpStatusCode.BadRequest, "Voix et rôle requis pour admettre ce membre.");

        if (role != UserRoleEnum.Singer && role != UserRoleEnum.Manager)
            throw new CustomException(HttpStatusCode.BadRequest, "Rôle non supporté pour une admission.");

        // Verifie AVANT toute mutation de la demande : en cas de refus, elle reste intacte et
        // donc EN FILE (decision produit — le plafond ne doit jamais faire perdre la demande).
        await _serviceLimitService.EnsureCanAddMemberAsync(spaceId, ct);

        var alreadyMember = await _context.SpaceMembers
            .AnyAsync(m => m.UserId == request.UserId && m.SpaceId == spaceId, ct);
        if (alreadyMember)
            throw new CustomException(HttpStatusCode.Conflict, "Ce demandeur est déjà membre de cette chorale.");

        // Appartenance, voix et rôle : séquence commune avec l'invitation nominative, tenue
        // par IMemberEnrollmentService. Rien n'est committé ici — le traitement de la demande
        // ci-dessous part dans le même SaveChanges, donc dans la même transaction.
        await _memberEnrollmentService.EnrollAsync(
            spaceId, request.UserId, MemberStatusEnum.Active, voicePart, role, ct);

        request.Status = MembershipRequestStatusEnum.Approved;
        request.HandledByUserId = _currentUserId;
        request.HandledAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);
        return _mapper.Map<MembershipRequestListItemViewModel>(request);
    }

    public async Task<MembershipRequestListItemViewModel> DeclineAsync(
        Guid spaceId, Guid id, DeclineRequestViewModel model, CancellationToken ct = default)
    {
        await EnsureManagerSpaceAsync(spaceId, ct);

        var request = await _context.MembershipRequests
            .Include(d => d.User)
            .FirstOrDefaultAsync(d => d.Id == id && d.SpaceId == spaceId
                && d.Status == MembershipRequestStatusEnum.Pending, ct)
            ?? throw new KeyNotFoundException($"Request {id} not found.");

        request.Status = MembershipRequestStatusEnum.Declined;
        request.DeclineReason = model.DeclineReason;
        request.HandledByUserId = _currentUserId;
        request.HandledAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);
        return _mapper.Map<MembershipRequestListItemViewModel>(request);
    }

    /// <summary>
    /// Meme message neutre, que le blocage vienne d'une demande deja EnAttente ou d'un refus
    /// recent (moins de 30 jours) — le demandeur ne doit jamais apprendre lequel des deux
    /// s'applique, ni le motif du refus.
    /// </summary>
    private async Task EnsureNoBlockerAsync(Guid spaceId, CancellationToken ct)
    {
        var enAttente = await _context.MembershipRequests
            .AsNoTracking()
            .AnyAsync(d => d.UserId == _currentUserId && d.SpaceId == spaceId
                && d.Status == MembershipRequestStatusEnum.Pending, ct);
        if (enAttente)
            throw new CustomException(HttpStatusCode.Conflict, MessageRequestBlocked);

        var threshold = DateTime.UtcNow.AddDays(-DeclineDelayDays);
        var refusRecent = await _context.MembershipRequests
            .AsNoTracking()
            .AnyAsync(d => d.UserId == _currentUserId && d.SpaceId == spaceId
                && d.Status == MembershipRequestStatusEnum.Declined
                && d.HandledAt != null && d.HandledAt >= threshold, ct);
        if (refusRecent)
            throw new CustomException(HttpStatusCode.Conflict, MessageRequestBlocked);
    }

    private async Task<MyRequestViewModel> MapWithSpaceNameAsync(MembershipRequest request, CancellationToken ct)
    {
        var viewModel = _mapper.Map<MyRequestViewModel>(request);
        viewModel.SpaceName = await _context.Choirs
            .AsNoTracking()
            .Where(c => c.Id == request.SpaceId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(ct) ?? string.Empty;
        return viewModel;
    }

    /// <summary>
    /// Reservee au Responsable de l'espace (matrice `02`) : ni Organizer ni SectionLeader
    /// n'invitent ni n'affectent.
    /// </summary>
    private async Task EnsureManagerSpaceAsync(Guid spaceId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_currentUserId))
            throw new CustomException(HttpStatusCode.Forbidden, "Action réservée au chef de chœur.");

        var rolesBySpace = await _spaceRoleResolverService.ResolveRolesAsync(_currentUserId, [spaceId], ct);
        if (rolesBySpace.TryGetValue(spaceId, out var roles) && roles.Contains(UserRoleEnum.Manager))
            return;

        throw new CustomException(HttpStatusCode.Forbidden, "Action réservée au chef de chœur.");
    }
}
