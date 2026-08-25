using System.Linq.Expressions;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Helpers;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.AdminEvents;
using Microsoft.EntityFrameworkCore;

namespace ChoraleBackEnd.Services.ChoirServices;

/// <summary>
/// Administration generale des events, transverse a tous les clients (`10-D23`). Lecture
/// seule : l'Admin n'ecrit ni sur le contenu ni sur le cycle de vie d'un evenement — voir
/// <c>EventController</c> pour les ecritures, reservees au gestionnaire de l'espace.
/// </summary>
public interface IAdminEventService
{
    Task<PagedListViewModel<AdminEventListItemViewModel>> GetPagedAsync(AdminEventsPagedFilterViewModel filter, CancellationToken ct = default);
    Task<AdminEventDetailViewModel> GetByIdAsync(Guid eventId, CancellationToken ct = default);
}

public sealed class AdminEventService : BaseService, IAdminEventService
{
    // Liste blanche de tri : ChoirName et ClientName traversent des navigations deja
    // chargees via Include ci-dessous. ParticipantCount reste hors liste : c'est un
    // agregat calcule apres pagination dans EnrichParticipantsAsync.
    private static readonly IReadOnlyDictionary<string, Expression<Func<Event, object?>>> EventsSortableColumns =
        new Dictionary<string, Expression<Func<Event, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Title"] = e => e.Title,
            ["StartDate"] = e => e.StartDate,
            ["ChoirName"] = e => e.Choir!.Name,
            ["ClientName"] = e => e.Space.Client.Name,
            ["Status"] = e => e.Status,
            ["Type"] = e => e.Type
        };

    public AdminEventService(IServiceProvider serviceProvider) : base(serviceProvider) { }

    public async Task<PagedListViewModel<AdminEventListItemViewModel>> GetPagedAsync(
        AdminEventsPagedFilterViewModel filter, CancellationToken ct = default)
    {
        // IgnoreQueryFilters + reapplication manuelle : un evenement soft-delete
        // (EventService.DeleteAsync) ne doit jamais resurgir, meme cote administration —
        // a la difference de la chorale, l'evenement porte deja un statut metier `Archive`
        // distinct pour son cycle de vie decide, donc IsDeleted garde ici son seul sens :
        // suppression. En revanche, depuis qu'SpaceConfiguration filtre sur
        // !Client.IsDeleted, le filtre par defaut d'Event (qui reference
        // e.Espace.IsDeleted) heriterait aussi de cette condition — un evenement dont le
        // client est supprime deviendrait invisible ici aussi. L'administration generale
        // doit au contraire pouvoir le retrouver (`10-D23`) : on ne reapplique donc que le
        // cycle de vie propre de l'evenement et de son espace, jamais celui du client.
        var query = _context.Events
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(e => !e.IsDeleted && !e.Space.IsDeleted)
            .Include(e => e.Choir)
            .Include(e => e.Space)
                .ThenInclude(space => space.Client)
            .AsQueryable();

        if (filter.ChoirId.HasValue)
            query = query.Where(e => e.ChoirId == filter.ChoirId.Value);

        if (filter.ClientId.HasValue)
            query = query.Where(e => e.Space.ClientId == filter.ClientId.Value);

        if (filter.Status.HasValue)
            query = query.Where(e => e.Status == filter.Status.Value);

        if (filter.Type.HasValue)
            query = query.Where(e => e.Type == filter.Type.Value);

        if (filter.Upcoming == true)
            query = query.Where(e => (e.EndDate ?? e.StartDate) >= DateTime.UtcNow);
        else if (filter.Upcoming == false)
            query = query.Where(e => (e.EndDate ?? e.StartDate) < DateTime.UtcNow);

        if (!string.IsNullOrWhiteSpace(filter.Filter))
            query = query.Where(e => e.Title.Contains(filter.Filter));

        var total = await query.CountAsync(ct);
        var items = await query
            .ApplySort(
                filter.SortActive, filter.SortDirection, EventsSortableColumns, e => e.Id,
                q => q.OrderBy(e => e.StartDate).ThenBy(e => e.Id))
            .Skip(filter.Offset)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        var viewModels = _mapper.Map<List<AdminEventListItemViewModel>>(items);
        await EnrichParticipantsAsync(viewModels, ct);

        return new PagedListViewModel<AdminEventListItemViewModel>
        {
            Items = viewModels,
            TotalCount = total,
            CurrentPage = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<AdminEventDetailViewModel> GetByIdAsync(Guid eventId, CancellationToken ct = default)
    {
        // Voir GetPagedAsync : IgnoreQueryFilters + reapplication manuelle pour ne pas perdre
        // la visibilite Admin sur un evenement dont le client est supprime.
        var evt = await _context.Events
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(e => !e.IsDeleted && !e.Space.IsDeleted)
            .Include(e => e.Choir)
            .Include(e => e.Space)
                .ThenInclude(space => space.Client)
            .FirstOrDefaultAsync(e => e.Id == eventId, ct)
            ?? throw new KeyNotFoundException($"Event {eventId} not found.");

        var viewModel = _mapper.Map<AdminEventDetailViewModel>(evt);
        viewModel.ParticipantCount = await CountParticipantsAsync(eventId, ct);

        return viewModel;
    }

    private Task<int> CountParticipantsAsync(Guid spaceId, CancellationToken ct)
        => _context.SpaceMembers.CountAsync(m => m.SpaceId == spaceId && m.Status != MemberStatusEnum.Archived, ct);

    /// <summary>
    /// Version groupée pour les lists : le nombre de participants de toute la page en une
    /// requête, au lieu d'une requête <b>par événement</b>.
    /// </summary>
    private async Task EnrichParticipantsAsync(List<AdminEventListItemViewModel> viewModels, CancellationToken ct)
    {
        if (viewModels.Count == 0) return;

        var ids = viewModels.Select(v => v.Id).ToList();

        var participants = await _context.SpaceMembers
            .Where(m => ids.Contains(m.SpaceId) && m.Status != MemberStatusEnum.Archived)
            .GroupBy(m => m.SpaceId)
            .Select(g => new { SpaceId = g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.SpaceId, x => x.N, ct);

        foreach (var viewModel in viewModels)
            viewModel.ParticipantCount = participants.GetValueOrDefault(viewModel.Id);
    }
}
