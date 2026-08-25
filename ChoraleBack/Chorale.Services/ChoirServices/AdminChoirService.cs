using System.Linq.Expressions;
using System.Net;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Helpers;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.AdminChoirs;
using Microsoft.EntityFrameworkCore;
using ChoirEntity = ChoraleBackEnd.Data.Entities.Choir;

namespace ChoraleBackEnd.Services.ChoirServices;


/// <summary>
/// Administration generale des chorales, transverse a tous les clients (`10-D23`). L'Admin
/// n'a ici que la lecture et les informations d'exploitation (nom, description, cycle de vie)
/// — jamais le contenu (chants, partitions, consignes), qui reste reserve au Responsable de
/// la chorale (`02` § Matrice).
/// </summary>
/// <remarks>
/// Migration 13 : le cycle de vie est desormais porte par <c>Choir.Status</c>
/// (<see cref="ChoirStatusEnum"/>), plus par <c>IsDeleted</c> — qui reprend son seul role,
/// la suppression. <c>ArchiveAsync</c>/<c>ReactivateAsync</c> (qui posaient/levaient
/// <c>IsDeleted</c>) sont remplaces par <see cref="ChangeStatusAsync"/>, qui passe par la
/// table de transitions <c>ChoirStateHelper</c>.
/// </remarks>
public interface IAdminChoirService
{
    Task<PagedListViewModel<AdminChoirListItemViewModel>> GetPagedAsync(AdminChoirsPagedFilterViewModel filter, CancellationToken ct = default);
    Task<AdminChoirDetailViewModel> GetByIdAsync(Guid choirId, CancellationToken ct = default);
    Task<AdminChoirImpactViewModel> GetArchiveImpactAsync(Guid choirId, CancellationToken ct = default);
    Task<AdminChoirDetailViewModel> UpdateAsync(AdminChoirUpdateViewModel model, CancellationToken ct = default);
    Task<AdminChoirDetailViewModel> ChangeStatusAsync(Guid choirId, ChoirStatusEnum status, CancellationToken ct = default);
}

public sealed class AdminChoirService : BaseService, IAdminChoirService
{
    private const int InactivityDaysThreshold = 30;

    // Liste blanche de tri : ClientName traverse la navigation Client (jointure), sans jamais
    // interpreter la chaine recue du client HTTP — voir TriHelper. MemberCount, SongCount
    // et LastActivityAt restent hors liste : ce sont des agregats calcules apres pagination
    // dans EnrichirAsync, pas des colonnes de la requete.
    private static readonly IReadOnlyDictionary<string, Expression<Func<ChoirEntity, object?>>> ChoirsSortableColumns =
        new Dictionary<string, Expression<Func<ChoirEntity, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = c => c.Name,
            ["ClientName"] = c => c.Client.Name,
            ["Status"] = c => c.Status,
            ["CreatedAt"] = c => c.CreatedAt
        };

    private readonly IAuditLogService _auditLogService;
    private readonly IServiceLimitService _serviceLimitService;

    public AdminChoirService(
        IServiceProvider serviceProvider,
        IAuditLogService auditLogService,
        IServiceLimitService serviceLimitService)
        : base(serviceProvider)
    {
        _auditLogService = auditLogService;
        _serviceLimitService = serviceLimitService;
    }

    public async Task<PagedListViewModel<AdminChoirListItemViewModel>> GetPagedAsync(
        AdminChoirsPagedFilterViewModel filter, CancellationToken ct = default)
    {
        // IgnoreQueryFilters + reapplication manuelle : depuis que SpaceConfiguration filtre
        // sur !Client.IsDeleted, le filtre par defaut de Chorale (qui reference c.Espace.IsDeleted)
        // heriterait aussi de cette condition sur le client — une chorale dont le client est
        // supprime deviendrait invisible ici, y compris pour l'Admin. C'est l'inverse de ce que
        // l'administration generale doit pouvoir faire (`10-D23`) : elle doit justement voir ce
        // qu'un client supprime laisse derriere lui. On ne garde donc que le cycle de vie propre
        // de la chorale et de son espace, jamais celui du client.
        var query = _context.Choirs.AsNoTracking().IgnoreQueryFilters()
            .Where(c => !c.IsDeleted && !c.Space.IsDeleted);

        if (filter.ClientId.HasValue)
            query = query.Where(c => c.ClientId == filter.ClientId.Value);

        if (filter.Status.HasValue)
            query = query.Where(c => c.Status == filter.Status.Value);

        if (!string.IsNullOrWhiteSpace(filter.Filter))
            query = query.Where(c => c.Name.Contains(filter.Filter));

        if (filter.InactiveFor30Days == true)
        {
            var threshold = DateTime.UtcNow.AddDays(-InactivityDaysThreshold);
            query = query.Where(c =>
                (c.UpdatedAt ?? c.CreatedAt) < threshold
                && !_context.Songs.Any(ch => ch.ChoirId == c.Id && (ch.UpdatedAt ?? ch.CreatedAt) >= threshold)
                && !_context.Events.Any(e => e.ChoirId == c.Id && (e.UpdatedAt ?? e.CreatedAt) >= threshold));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .ApplySort(
                filter.SortActive, filter.SortDirection, ChoirsSortableColumns, c => c.Id,
                q => q.OrderBy(c => c.Name).ThenBy(c => c.Id))
            .Skip(filter.Offset)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        var viewModels = _mapper.Map<List<AdminChoirListItemViewModel>>(items);
        await EnrichAsync(viewModels, items, ct);

        return new PagedListViewModel<AdminChoirListItemViewModel>
        {
            Items = viewModels,
            TotalCount = total,
            CurrentPage = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<AdminChoirDetailViewModel> GetByIdAsync(Guid choirId, CancellationToken ct = default)
    {
        // Voir GetPagedAsync : IgnoreQueryFilters + reapplication manuelle pour ne pas perdre
        // la visibilite Admin sur une chorale dont le client a ete supprime.
        var choir = await _context.Choirs
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(c => !c.IsDeleted && !c.Space.IsDeleted)
            .Include(c => c.Client)
            .FirstOrDefaultAsync(c => c.Id == choirId, ct)
            ?? throw new KeyNotFoundException($"Choir {choirId} not found.");

        var viewModel = _mapper.Map<AdminChoirDetailViewModel>(choir);

        viewModel.MemberCount = await CountMembersAsync(choirId, ct);
        viewModel.SongCount = await _context.Songs.CountAsync(c => c.ChoirId == choirId, ct);
        viewModel.EventCount = await _context.Events.CountAsync(e => e.ChoirId == choirId, ct);

        var usage = await _serviceLimitService.GetUsageAsync(choir.ClientId, ct);
        viewModel.ClientChoirLimit = usage.ChoirLimit;
        viewModel.ClientChoirCount = usage.Choirs;
        viewModel.ClientMemberLimit = usage.MemberLimit;
        viewModel.ClientMemberCount = usage.Members;
        viewModel.ClientStorageQuotaBytes = usage.StorageQuotaBytes;
        viewModel.ClientUsedStorageBytes = usage.StorageOctets;

        return viewModel;
    }

    public async Task<AdminChoirImpactViewModel> GetArchiveImpactAsync(Guid choirId, CancellationToken ct = default)
    {
        await LoadAsync(choirId, ct);

        // Sans IgnoreQueryFilters : le filtre par defaut exclut deja les entites soft-deletes,
        // ce qui est exactement le compte exact attendu (`Members`, `Songs`, `Events`).
        return new AdminChoirImpactViewModel
        {
            MemberCount = await CountMembersAsync(choirId, ct),
            SongCount = await _context.Songs.CountAsync(c => c.ChoirId == choirId, ct),
            EventCount = await _context.Events.CountAsync(e => e.ChoirId == choirId, ct)
        };
    }

    public async Task<AdminChoirDetailViewModel> UpdateAsync(AdminChoirUpdateViewModel model, CancellationToken ct = default)
    {
        var choir = await LoadAsync(model.Id, ct);

        choir.Name = model.Name;
        choir.Description = model.Description;

        _auditLogService.Record("AdminChoirUpdated", "Choir", choir.Id.ToString());
        await _context.SaveChangesAsync(ct);

        return await GetByIdAsync(choir.Id, ct);
    }

    public async Task<AdminChoirDetailViewModel> ChangeStatusAsync(
        Guid choirId, ChoirStatusEnum status, CancellationToken ct = default)
    {
        // Defense en profondeur, meme raison que ClientService.ChangeStatusAsync : la
        // validation du modele borne deja la plage cote controleur, mais ce service affecte
        // le statut directement.
        if (!Enum.IsDefined(status))
            throw new CustomException(HttpStatusCode.BadRequest, "Statut de chorale inconnu.");

        var choir = await LoadAsync(choirId, ct);

        if (choir.Status == status)
            return await GetByIdAsync(choir.Id, ct);

        if (!ChoirStateHelper.IsTransitionAllowed(choir.Status, status))
            throw new CustomException(HttpStatusCode.Conflict,
                "Transition de statut interdite depuis l'état actuel de la chorale.");

        // Toute reactivation depuis Archive doit revalider la place disponible : une chorale
        // Archive n'occupe pas le plafond (ServiceLimitService.CountChoirsAsync), donc la
        // reactivate peut faire ressurgir une consommation qui depasse un plafond abaisse
        // entre-temps. Le refus est explicite et chiffre (comportement du lot 3, ne pas le
        // regresser), sans jamais amputer l'existant : la verification a lieu AVANT toute
        // ecriture.
        if (status == ChoirStatusEnum.Published && choir.Status == ChoirStatusEnum.Archived)
            await _serviceLimitService.EnsureCanCreateChoirAsync(choir.ClientId, ct);

        choir.Status = status;

        _auditLogService.Record("AdminChoirStatusChanged", "Choir", choir.Id.ToString(),
            $"Nouveau status : {status}.");
        await _context.SaveChangesAsync(ct);

        return await GetByIdAsync(choir.Id, ct);
    }

    /// <summary>
    /// Count de personnes distinctes membres de l'espace-chorale, archivees exclues — meme
    /// regle que <c>ServiceLimitService.CountMembersAsync</c>.
    /// </summary>
    private Task<int> CountMembersAsync(Guid choirId, CancellationToken ct)
        => _context.SpaceMembers
            .Where(m => m.ChoirId == choirId && m.SpaceId == choirId && m.Status != MemberStatusEnum.Archived)
            .Select(m => m.UserId)
            .Distinct()
            .CountAsync(ct);

    /// <summary>
    /// Version groupée pour les lists : la consommation de toute la page en quatre requêtes,
    /// au lieu de quatre requêtes <b>par chorale</b> — même piège et même correctif que
    /// <c>ClientService.EnrichUsagesAsync</c>.
    /// </summary>
    private async Task EnrichAsync(
        List<AdminChoirListItemViewModel> viewModels, List<ChoirEntity> choirs, CancellationToken ct)
    {
        if (viewModels.Count == 0) return;

        var ids = viewModels.Select(v => v.Id).ToList();
        var clientIds = viewModels.Select(v => v.ClientId).Distinct().ToList();
        var choirsById = choirs.ToDictionary(c => c.Id, c => c);

        var clientsNames = await _context.Clients
            .IgnoreQueryFilters()
            .Where(c => clientIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var members = (await _context.SpaceMembers
                .Where(m => m.ChoirId != null && ids.Contains(m.ChoirId.Value)
                            && m.SpaceId == m.ChoirId && m.Status != MemberStatusEnum.Archived)
                .Select(m => new { ChoirId = m.ChoirId!.Value, m.UserId })
                .Distinct()
                .ToListAsync(ct))
            .GroupBy(x => x.ChoirId)
            .ToDictionary(g => g.Key, g => g.Count());

        var songs = await _context.Songs
            .Where(c => ids.Contains(c.ChoirId))
            .GroupBy(c => c.ChoirId)
            .Select(g => new { ChoirId = g.Key, N = g.Count(), Last = g.Max(c => c.UpdatedAt ?? c.CreatedAt) })
            .ToDictionaryAsync(x => x.ChoirId, x => x, ct);

        var maintenant = DateTime.UtcNow;
        var events = await _context.Events
            .Where(e => e.ChoirId != null && ids.Contains(e.ChoirId.Value))
            .GroupBy(e => e.ChoirId!.Value)
            .Select(g => new
            {
                ChoirId = g.Key,
                Upcoming = g.Count(e => (e.EndDate ?? e.StartDate) >= maintenant),
                Last = g.Max(e => e.UpdatedAt ?? e.CreatedAt)
            })
            .ToDictionaryAsync(x => x.ChoirId, x => x, ct);

        foreach (var viewModel in viewModels)
        {
            viewModel.ClientName = clientsNames.GetValueOrDefault(viewModel.ClientId, string.Empty);
            viewModel.MemberCount = members.GetValueOrDefault(viewModel.Id);

            var lastActivity = choirsById.TryGetValue(viewModel.Id, out var choir)
                ? choir.UpdatedAt ?? choir.CreatedAt
                : DateTime.MinValue;

            if (songs.TryGetValue(viewModel.Id, out var songStats))
            {
                viewModel.SongCount = songStats.N;
                if (songStats.Last > lastActivity) lastActivity = songStats.Last;
            }

            if (events.TryGetValue(viewModel.Id, out var eventStats))
            {
                viewModel.UpcomingEventCount = eventStats.Upcoming;
                if (eventStats.Last > lastActivity) lastActivity = eventStats.Last;
            }

            viewModel.LastActivityAt = lastActivity;
        }
    }

    // Voir GetPagedAsync : IgnoreQueryFilters + reapplication manuelle pour ne pas perdre
    // la visibilite Admin sur une chorale dont le client a ete supprime.
    private async Task<ChoirEntity> LoadAsync(Guid choirId, CancellationToken ct)
        => await _context.Choirs
               .IgnoreQueryFilters()
               .Where(c => !c.IsDeleted && !c.Space.IsDeleted)
               .FirstOrDefaultAsync(c => c.Id == choirId, ct)
           ?? throw new KeyNotFoundException($"Choir {choirId} not found.");
}
