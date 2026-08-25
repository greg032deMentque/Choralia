using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Helpers;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.ViewModels.AdminDashboard;
using Microsoft.EntityFrameworkCore;

namespace ChoraleBackEnd.Services.ChoirServices;

/// <summary>
/// Indicateurs du tableau de bord d'administration generale (`10-D30`). Transverse a tous les
/// clients, comme <c>AdminChoirService</c>/<c>AdminSongService</c>/<c>AdminEventService</c>.
/// </summary>
/// <remarks>
/// Un seul point d'entry, <see cref="GetKpiAsync"/> : chaque indicateur est calcule par une
/// ou plusieurs requetes agregees (comptage, regroupement, somme), jamais par un acces au
/// contexte a l'interieur d'une boucle sur les entites — meme discipline que
/// <c>ClientService.EnrichUsagesAsync</c>.
/// </remarks>
public interface IAdminDashboardService
{
    Task<AdminDashboardKpiViewModel> GetKpiAsync(CancellationToken ct = default);
}

public sealed class AdminDashboardService : BaseService, IAdminDashboardService
{
    private const int ChoirInactivityDaysThreshold = 30;
    private const int UpcomingEventsDays = 30;
    private const double NearCapThreshold = 0.8;

    private static readonly Guid ClientWithoutStructureId = Client.ClientTechnique.WithoutStructure;

    public AdminDashboardService(IServiceProvider serviceProvider) : base(serviceProvider) { }

    public async Task<AdminDashboardKpiViewModel> GetKpiAsync(CancellationToken ct = default)
        => new()
        {
            Clients = await ComputeClientsAsync(ct),
            Choirs = await ComputeChoirsAsync(ct),
            Users = await ComputeUsersAsync(ct),
            InactiveChoirs = await ComputeInactiveChoirsAsync(ct),
            NotStartedClients = await ComputeNotStartedClientsAsync(ct),
            ClientsNearCap = await ComputeClientsNearCapAsync(ct),
            TotalStorageBytes = await ComputeStorageTotalAsync(ct),
            Songs = await ComputeSongsAsync(ct),
            UpcomingEvents30Days = await ComputeEventsUpcomingAsync(ct),
            EventsWithoutStructureAnomaly = await ComputeEventsWithoutStructureAnomalyAsync(ct)
        };

    private async Task<ClientsKpiViewModel> ComputeClientsAsync(CancellationToken ct)
    {
        var byStatus = await _context.Clients
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.N, ct);

        return new ClientsKpiViewModel
        {
            Total = byStatus.Values.Sum(),
            Active = byStatus.GetValueOrDefault(ClientStatusEnum.Active),
            Suspended = byStatus.GetValueOrDefault(ClientStatusEnum.Suspended),
            Archived = byStatus.GetValueOrDefault(ClientStatusEnum.Archived)
        };
    }

    private async Task<ChoirsKpiViewModel> ComputeChoirsAsync(CancellationToken ct)
    {
        var byStatus = await _context.Choirs
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.N, ct);

        return new ChoirsKpiViewModel
        {
            Total = byStatus.Values.Sum(),
            Draft = byStatus.GetValueOrDefault(ChoirStatusEnum.Draft),
            Published = byStatus.GetValueOrDefault(ChoirStatusEnum.Published),
            Cancelled = byStatus.GetValueOrDefault(ChoirStatusEnum.Cancelled),
            Archived = byStatus.GetValueOrDefault(ChoirStatusEnum.Archived)
        };
    }

    private async Task<UsersKpiViewModel> ComputeUsersAsync(CancellationToken ct)
        => new()
        {
            Total = await _context.Users.CountAsync(ct),
            Active = await _context.Users.CountAsync(u => u.IsActive, ct),
            InactiveInvitees = await _context.Users.CountAsync(u => u.IsGuestAccount && !u.EmailConfirmed, ct)
        };

    /// <summary>
    /// Une chorale operationnelle (<c>Publie</c> ou <c>Annule</c>) est inactive depuis plus de
    /// 30 jours quand la derniere activite mesuree de ses membres actifs (<c>LastActive</c>,
    /// replie sur <c>LastConnection</c> puis <c>CreatedAt</c> — meme repli que
    /// <c>GuestAccountLifecycleService</c>) est anterieure au seuil. Une chorale sans aucun
    /// membre actif n'a pas de mesure : elle n'est volontairement pas comptee ici, pour ne pas
    /// confondre « inactive » et « vide » (cette derniere remonte via les clients non demarres).
    /// </summary>
    private async Task<InactiveChoirsKpiViewModel> ComputeInactiveChoirsAsync(CancellationToken ct)
    {
        var threshold = DateTime.UtcNow.AddDays(-ChoirInactivityDaysThreshold);

        var operationalChoirIds = await _context.Choirs
            .Where(c => c.Status == ChoirStatusEnum.Published || c.Status == ChoirStatusEnum.Cancelled)
            .Select(c => c.Id)
            .ToListAsync(ct);

        if (operationalChoirIds.Count == 0)
            return new InactiveChoirsKpiViewModel();

        var lastActivityByChoir = await _context.SpaceMembers
            .Where(m => m.ChoirId != null
                        && m.SpaceId == m.ChoirId
                        && m.Status == MemberStatusEnum.Active
                        && operationalChoirIds.Contains(m.ChoirId!.Value))
            .Select(m => new { ChoirId = m.ChoirId!.Value, LastActivity = m.User.LastActive ?? m.User.LastConnection ?? m.User.CreatedAt })
            .GroupBy(x => x.ChoirId)
            .Select(g => new { ChoirId = g.Key, LastActivity = g.Max(x => x.LastActivity) })
            .ToDictionaryAsync(x => x.ChoirId, x => x.LastActivity, ct);

        var inactiveChoirIds = operationalChoirIds
            .Where(id => lastActivityByChoir.TryGetValue(id, out var last) && last < threshold)
            .ToList();

        return new InactiveChoirsKpiViewModel { Count = inactiveChoirIds.Count, ChoirIds = inactiveChoirIds };
    }

    /// <summary>
    /// Client non archive dont aucune chorale ne porte de chant, et dont aucune chorale n'a de
    /// membre (espace-chorale, archives exclus — meme peimetre que
    /// <c>ServiceLimitService.CountMembersAsync</c>). Un client sans aucune chorale tombe
    /// naturellement ici : il n'apparait dans aucun des deux ensembles.
    /// </summary>
    private async Task<NotStartedClientsKpiViewModel> ComputeNotStartedClientsAsync(CancellationToken ct)
    {
        var candidateClients = await _context.Clients
            .Where(c => c.Status != ClientStatusEnum.Archived)
            .Select(c => c.Id)
            .ToListAsync(ct);

        if (candidateClients.Count == 0)
            return new NotStartedClientsKpiViewModel();

        var clientIdsWithSong = await _context.Songs
            .Select(c => c.Choir.ClientId)
            .Distinct()
            .ToListAsync(ct);

        var clientIdsWithMember = await _context.SpaceMembers
            .Where(m => m.ChoirId != null && m.SpaceId == m.ChoirId && m.Status != MemberStatusEnum.Archived)
            .Select(m => m.Choir!.ClientId)
            .Distinct()
            .ToListAsync(ct);

        var withActivity = clientIdsWithSong.Concat(clientIdsWithMember).ToHashSet();
        var notStarted = candidateClients.Where(id => !withActivity.Contains(id)).ToList();

        return new NotStartedClientsKpiViewModel { Count = notStarted.Count, ClientIds = notStarted };
    }

    /// <summary>
    /// Consommation de chaque client agregee en quatre requetes groupees (pas une par
    /// client), puis comparee a ses quatre plafonds en memoire. Un plafond a 0 est exclu du
    /// calcul pour ce client — sinon toute consommation, meme nulle, se traduirait par un
    /// taux de 100 %.
    /// </summary>
    private async Task<ClientsNearCapKpiViewModel> ComputeClientsNearCapAsync(CancellationToken ct)
    {
        var clients = await _context.Clients
            .Where(c => c.Status != ClientStatusEnum.Archived)
            .Select(c => new
            {
                c.Id,
                c.ChoirLimit,
                c.MemberLimit,
                c.StorageQuotaBytes,
                c.MaxFileSizeBytes
            })
            .ToListAsync(ct);

        if (clients.Count == 0)
            return new ClientsNearCapKpiViewModel();

        var clientIds = clients.Select(c => c.Id).ToList();

        var choirsByClient = await _context.Choirs
            .Where(c => clientIds.Contains(c.ClientId) && c.Status != ChoirStatusEnum.Archived)
            .GroupBy(c => c.ClientId)
            .Select(g => new { ClientId = g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.ClientId, x => x.N, ct);

        var membersByClient = (await _context.SpaceMembers
                .Where(m => m.ChoirId != null && m.SpaceId == m.ChoirId && m.Status != MemberStatusEnum.Archived)
                .Join(_context.Choirs.Where(c => clientIds.Contains(c.ClientId)),
                    m => m.ChoirId, c => c.Id,
                    (m, c) => new { c.ClientId, m.UserId })
                .Distinct()
                .ToListAsync(ct))
            .GroupBy(x => x.ClientId)
            .ToDictionary(g => g.Key, g => g.Count());

        var storageByClient = await ComputeStoragePerClientAsync(clientIds, ct);
        var maxFileByClient = await ComputeMaxFileSizePerClientAsync(clientIds, ct);

        var nearCap = clients
            .Where(c =>
                ExceedsThreshold(choirsByClient.GetValueOrDefault(c.Id), c.ChoirLimit)
                || ExceedsThreshold(membersByClient.GetValueOrDefault(c.Id), c.MemberLimit)
                || ExceedsThreshold(storageByClient.GetValueOrDefault(c.Id), c.StorageQuotaBytes)
                || ExceedsThreshold(maxFileByClient.GetValueOrDefault(c.Id), c.MaxFileSizeBytes))
            .Select(c => c.Id)
            .ToList();

        return new ClientsNearCapKpiViewModel { Count = nearCap.Count, ClientIds = nearCap };
    }

    private async Task<Dictionary<Guid, long>> ComputeStoragePerClientAsync(List<Guid> clientIds, CancellationToken ct)
    {
        var scores = await _context.Scores
            .IgnoreQueryFilters()
            .Join(_context.Choirs.Where(c => clientIds.Contains(c.ClientId)),
                p => p.Song.ChoirId, c => c.Id,
                (p, c) => new { c.ClientId, p.SizeBytes })
            .GroupBy(x => x.ClientId)
            .Select(g => new { ClientId = g.Key, Total = g.Sum(x => x.SizeBytes) })
            .ToDictionaryAsync(x => x.ClientId, x => x.Total, ct);

        var recordings = await _context.Recordings
            .IgnoreQueryFilters()
            .Join(_context.Choirs.Where(c => clientIds.Contains(c.ClientId)),
                e => e.ChoirOwnerId, c => c.Id,
                (e, c) => new { c.ClientId, e.SizeBytes })
            .GroupBy(x => x.ClientId)
            .Select(g => new { ClientId = g.Key, Total = g.Sum(x => x.SizeBytes) })
            .ToDictionaryAsync(x => x.ClientId, x => x.Total, ct);

        var result = new Dictionary<Guid, long>();
        foreach (var id in clientIds)
            result[id] = scores.GetValueOrDefault(id) + recordings.GetValueOrDefault(id);

        return result;
    }

    private async Task<Dictionary<Guid, long>> ComputeMaxFileSizePerClientAsync(List<Guid> clientIds, CancellationToken ct)
    {
        var maxScores = await _context.Scores
            .IgnoreQueryFilters()
            .Join(_context.Choirs.Where(c => clientIds.Contains(c.ClientId)),
                p => p.Song.ChoirId, c => c.Id,
                (p, c) => new { c.ClientId, p.SizeBytes })
            .GroupBy(x => x.ClientId)
            .Select(g => new { ClientId = g.Key, Max = g.Max(x => x.SizeBytes) })
            .ToDictionaryAsync(x => x.ClientId, x => x.Max, ct);

        var maxRecordings = await _context.Recordings
            .IgnoreQueryFilters()
            .Join(_context.Choirs.Where(c => clientIds.Contains(c.ClientId)),
                e => e.ChoirOwnerId, c => c.Id,
                (e, c) => new { c.ClientId, e.SizeBytes })
            .GroupBy(x => x.ClientId)
            .Select(g => new { ClientId = g.Key, Max = g.Max(x => x.SizeBytes) })
            .ToDictionaryAsync(x => x.ClientId, x => x.Max, ct);

        var result = new Dictionary<Guid, long>();
        foreach (var id in clientIds)
            result[id] = Math.Max(maxScores.GetValueOrDefault(id), maxRecordings.GetValueOrDefault(id));

        return result;
    }

    private static bool ExceedsThreshold(long consumed, long limit)
        => limit > 0 && consumed > limit * NearCapThreshold;

    private async Task<long> ComputeStorageTotalAsync(CancellationToken ct)
    {
        var scores = await _context.Scores.IgnoreQueryFilters().SumAsync(p => (long?)p.SizeBytes, ct) ?? 0L;
        var recordings = await _context.Recordings.IgnoreQueryFilters().SumAsync(e => (long?)e.SizeBytes, ct) ?? 0L;
        return scores + recordings;
    }

    /// <summary>
    /// Meme regroupement d'AFFICHAGE que <c>AdminSongService</c> — voir <see cref="SongKeyHelper"/>.
    /// Charge en une seule requete groupee (jointures Chorale/Client traduites en SQL), puis
    /// regroupe en memoire : la cle de regroupement n'est pas traduisible en SQL.
    /// </summary>
    private async Task<SongsKpiViewModel> ComputeSongsAsync(CancellationToken ct)
    {
        var rows = await _context.Songs
            .AsNoTracking()
            .Where(c => c.Choir.Status != ChoirStatusEnum.Archived)
            .Select(c => new { c.Id, c.Title, c.Composer, c.ChoirId })
            .ToListAsync(ct);

        var duplicateGroupCount = rows
            .GroupBy(l => SongKeyHelper.ComputeKey(l.Id, l.Title, l.Composer))
            .Count(g => g.Select(l => l.ChoirId).Distinct().Count() > 1);

        return new SongsKpiViewModel { Total = rows.Count, DuplicateGroups = duplicateGroupCount };
    }

    private Task<int> ComputeEventsUpcomingAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var horizon = now.AddDays(UpcomingEventsDays);

        return _context.Events.CountAsync(
            e => e.Status == EventStatusEnum.Published && e.StartDate >= now && e.StartDate <= horizon, ct);
    }

    private async Task<EventsWithoutStructureAnomalyViewModel> ComputeEventsWithoutStructureAnomalyAsync(CancellationToken ct)
    {
        var ids = await _context.Events
            .Where(e => e.Space.ClientId == ClientWithoutStructureId)
            .Select(e => e.Id)
            .ToListAsync(ct);

        return new EventsWithoutStructureAnomalyViewModel { Count = ids.Count, EventIds = ids };
    }
}
