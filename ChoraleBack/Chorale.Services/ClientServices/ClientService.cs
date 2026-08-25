using System.Linq.Expressions;
using System.Net;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Helpers;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.Clients;
using Microsoft.EntityFrameworkCore;
using ChoirEntity = ChoraleBackEnd.Data.Entities.Choir;

namespace ChoraleBackEnd.Services.ClientServices;

public interface IClientService
{
    Task<PagedListViewModel<ClientViewModel>> GetPagedAsync(ClientsPagedFilterViewModel pagination, CancellationToken ct = default);
    Task<ClientViewModel> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ClientViewModel> CreateAsync(CreateClientViewModel model, CancellationToken ct = default);
    Task<ClientViewModel> UpdateAsync(UpdateClientViewModel model, CancellationToken ct = default);
    Task<ClientViewModel> UpdateLimitsAsync(UpdateClientLimitsViewModel model, CancellationToken ct = default);
    Task<ClientViewModel> ChangeStatusAsync(ChangeClientStatusViewModel model, CancellationToken ct = default);
    Task<ClientViewModel> ReactivateAsync(Guid id, CancellationToken ct = default);
    Task<SuspensionImpactViewModel> GetImpactSuspensionAsync(Guid id, CancellationToken ct = default);
    Task AssignManagerAsync(Guid clientId, AssignClientManagerViewModel model, CancellationToken ct = default);
    Task RemoveManagerAsync(Guid clientId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Responsables du client, page ecran qui manquait a la designation/au retrait (aucun
    /// des deux n'etait exploitable sans pouvoir d'abord voir qui est deja responsable).
    /// Meme niveau d'acces que <see cref="AssignManagerAsync"/>.
    /// </summary>
    Task<PagedListViewModel<ClientManagerListItemViewModel>> GetManagersAsync(
        Guid clientId, PaginateViewModel pagination, CancellationToken ct = default);

    /// <summary>
    /// Chorales du client, avec leur niveau d'consommation — ecran central de la zone « Ma structure »
    /// du <c>ManagerClient</c> (`10-D23`).
    /// </summary>
    Task<PagedListViewModel<ClientChoirListItemViewModel>> GetChoirsAsync(
        Guid clientId, PaginateViewModel pagination, CancellationToken ct = default);

    /// <summary>
    /// Fiche detail d'une chorale du client — ecran de detail de la zone « Ma structure »
    /// (`10-D23`). A la difference de <see cref="GetChoirsAsync"/>, une chorale Archivee reste
    /// lisible ici : c'est le seul moyen pour le ManagerClient de la reactiver depuis sa fiche.
    /// </summary>
    Task<ClientChoirDetailViewModel> GetChoirAsync(
        Guid clientId, Guid choirId, CancellationToken ct = default);

    /// <summary>
    /// Changement de statut d'une chorale par le ManagerClient de son client (`10-D23`). Meme
    /// table de transitions que l'administration generale (<see cref="ChoirStateHelper"/>),
    /// sans sous-ensemble restreint invente pour ce role.
    /// </summary>
    Task<ClientChoirDetailViewModel> ChangeChoirStatusAsync(
        Guid clientId, Guid choirId, ChoirStatusEnum status, CancellationToken ct = default);
}

public sealed class ClientService : BaseService, IClientService
{
    // Listes blanches de tri : ChoirCount/MemberCount/UsedStorageBytes et
    // SongCount/UpcomingEventCount restent hors liste — ce sont des agregats calcules
    // apres pagination (EnrichUsagesAsync / EnrichChoirsAsync), pas des colonnes
    // de la requete de base.
    private static readonly IReadOnlyDictionary<string, Expression<Func<Client, object?>>> ClientsSortableColumns =
        new Dictionary<string, Expression<Func<Client, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = c => c.Name,
            ["Status"] = c => c.Status,
            ["CreatedAt"] = c => c.CreatedAt
        };

    private static readonly IReadOnlyDictionary<string, Expression<Func<ChoirEntity, object?>>> ClientChoirsSortableColumns =
        new Dictionary<string, Expression<Func<ChoirEntity, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = c => c.Name,
            ["CreatedAt"] = c => c.CreatedAt
        };

    // Bornes de ClientsPagedFilterViewModel.ProcheDuPlafond, alignees sur
    // AdminDashboardService.CalculerClientsProchesPlafondAsync (meme regle de seuil,
    // evaluee ici a la demande plutot que figee a l'instant du tableau de bord).
    private const double NearCapThreshold = 0.8;

    // Revalidee ici en plus de [MaxLength(200)] sur le ViewModel : un appel direct au
    // service (tests, futur appelant interne) ne doit pas pouvoir contourner la borne.
    private const int MaxClientIds = 200;

    private readonly IAuditLogService _auditLogService;
    private readonly IServiceLimitService _serviceLimitService;
    private readonly IClientRoleResolverService _clientRoleResolverService;

    public ClientService(
        IServiceProvider serviceProvider,
        IAuditLogService auditLogService,
        IServiceLimitService serviceLimitService,
        IClientRoleResolverService clientRoleResolverService)
        : base(serviceProvider)
    {
        _auditLogService = auditLogService;
        _serviceLimitService = serviceLimitService;
        _clientRoleResolverService = clientRoleResolverService;
    }

    public async Task<PagedListViewModel<ClientViewModel>> GetPagedAsync(
        ClientsPagedFilterViewModel pagination, CancellationToken ct = default)
    {
        // GetPaged est Admin-only depuis le lot 2 (`ClientController`, policy Roles=Admin) : la
        // restriction par ResponsableClient qui vivait ici (filtrer sur ses propres clients)
        // etait devenue du code mort — ce chemin n'est plus jamais atteint que par un Admin.
        // Elle n'est pas reutilisee telle quelle : la zone « Ma structure » du ResponsableClient
        // ne liste pas plusieurs CLIENTS (il ne gere que le sien), elle liste ses CHORALES —
        // voir `GetChoirsAsync`, le veritable ecran central de cette zone (lot 3, `10-D23`).
        var query = _context.Clients.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(pagination.Filter))
            query = query.Where(c => c.Name.Contains(pagination.Filter));

        if (pagination.Status is { } statusFiltre)
            query = query.Where(c => c.Status == statusFiltre);

        // Liste presente mais vide : le tableau de bord designe explicitement « ces
        // identifiants precis », qui n'existent pas — zero result, jamais un repli sur
        // la liste complete (une tuile « non demarres » vide ne doit pas afficher tous les
        // clients). Liste absente (null) : filtre inactive, comportement inchange.
        if (pagination.ClientIds is { } clientIds)
        {
            if (clientIds.Count > MaxClientIds)
                throw new CustomException(HttpStatusCode.BadRequest,
                    $"Trop d'identifiants transmis : {clientIds.Count} sur un maximum de {MaxClientIds}.");

            query = query.Where(c => clientIds.Contains(c.Id));
        }

        if (pagination.NearCap == true)
        {
            var idsNearCap = await ComputeClientIdsNearCapAsync(query, ct);
            query = query.Where(c => idsNearCap.Contains(c.Id));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            // Tri par defaut inchange (pas de ThenBy(Id) ici avant cette correction) : le
            // preserver a l'identique quand SortActive est absent est la garantie de
            // non-regression demandee. Le departage sur Id n'est ajoute que lorsqu'un tri
            // explicite est demande (voir TriHelper).
            .ApplySort(
                pagination.SortActive, pagination.SortDirection, ClientsSortableColumns, c => c.Id,
                q => q.OrderBy(c => c.Name))
            .Skip(pagination.Offset)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        var viewModels = _mapper.Map<List<ClientViewModel>>(items);
        await EnrichUsagesAsync(viewModels, ct);

        return new PagedListViewModel<ClientViewModel>
        {
            Items = viewModels,
            TotalCount = total,
            CurrentPage = pagination.Page,
            PageSize = pagination.PageSize
        };
    }

    public async Task<ClientViewModel> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var client = await LoadAsync(id, ct);
        var viewModel = _mapper.Map<ClientViewModel>(client);
        await EnrichUsageAsync(viewModel, ct);
        return viewModel;
    }

    public async Task<ClientViewModel> CreateAsync(
        CreateClientViewModel model, CancellationToken ct = default)
    {
        EnsureAdmin();

        // Le nom est un libelle d'exploitation, pas une cle : il reste obligatoire, mais
        // n'a plus a etre unique parmi les clients actifs (`04` § Client).
        if (string.IsNullOrWhiteSpace(model.Name))
            throw new CustomException(HttpStatusCode.BadRequest, "Le nom du client est requis.");

        var client = new Client
        {
            Id = ChoraleDbContext.NewIdGuid(),
            Name = model.Name,
            ContactName = model.ContactName,
            ContactEmail = model.ContactEmail,
            Status = ClientStatusEnum.Active,
            ChoirLimit = Client.DefaultLimits.Choirs,
            MemberLimit = Client.DefaultLimits.Members,
            StorageQuotaBytes = Client.DefaultLimits.StorageOctets,
            MaxFileSizeBytes = Client.DefaultLimits.FileSizeBytes,
            IsDeleted = false
        };

        _context.Clients.Add(client);
        _auditLogService.Record("ClientCreated", nameof(Client), client.Id.ToString(), client.Name);
        await _context.SaveChangesAsync(ct);

        return await GetByIdAsync(client.Id, ct);
    }

    public async Task<ClientViewModel> UpdateAsync(
        UpdateClientViewModel model, CancellationToken ct = default)
    {
        EnsureAdmin();

        var client = await LoadAsync(model.Id, ct);

        if (string.IsNullOrWhiteSpace(model.Name))
            throw new CustomException(HttpStatusCode.BadRequest, "Le nom du client est requis.");

        client.Name = model.Name;
        client.ContactName = model.ContactName;
        client.ContactEmail = model.ContactEmail;

        await _context.SaveChangesAsync(ct);
        return await GetByIdAsync(client.Id, ct);
    }

    public async Task<ClientViewModel> UpdateLimitsAsync(
        UpdateClientLimitsViewModel model, CancellationToken ct = default)
    {
        // Les plafonds sont fixes par l'administration generale SEULE (decision produit,
        // `10-D23`) : le ResponsableClient les consulte via GetByIdAsync, jamais en ecriture.
        // La policy HTTP restreint deja Update a Roles=Admin, mais un appel direct au service
        // ne doit pas contourner cette regle.
        EnsureAdmin();

        var client = await LoadAsync(model.Id, ct);

        client.ChoirLimit = model.ChoirLimit;
        client.MemberLimit = model.MemberLimit;
        client.StorageQuotaBytes = model.StorageQuotaBytes;
        client.MaxFileSizeBytes = model.MaxFileSizeBytes;

        // Abaisser un plafond sous la consommation actuelle n'ampute rien : l'existant est
        // conserve, seules les creations nouvelles seront refusees (`04` § Client). On trace
        // le cas, parce qu'il merite d'etre vu par l'exploitation.
        var usage = await _serviceLimitService.GetUsageAsync(client.Id, ct);
        if (usage.Choirs > model.ChoirLimit
            || usage.Members > model.MemberLimit
            || usage.StorageOctets > model.StorageQuotaBytes)
        {
            _auditLogService.Record("ClientLimitsBelowUsage", nameof(Client), client.Id.ToString(),
                $"Choirs {usage.Choirs}/{model.ChoirLimit}, "
                + $"members {usage.Members}/{model.MemberLimit}, "
                + $"storage {usage.StorageOctets}/{model.StorageQuotaBytes} octets.");
        }

        _auditLogService.Record("ClientLimitsUpdated", nameof(Client), client.Id.ToString());
        await _context.SaveChangesAsync(ct);
        return await GetByIdAsync(client.Id, ct);
    }

    public async Task<ClientViewModel> ChangeStatusAsync(
        ChangeClientStatusViewModel model, CancellationToken ct = default)
    {
        EnsureAdmin();

        // Defense en profondeur : la validation du modele borne deja la plage, mais ce
        // service affecte le statut directement, sans le `switch` exhaustif qui protege
        // ChangeRoleAsync. Une valeur hors enum arrivant par un autre chemin serait
        // persistee et sortirait le client de tout etat connu.
        if (model.Status is not { } status || !Enum.IsDefined(status))
            throw new CustomException(HttpStatusCode.BadRequest, "Statut de client inconnu.");

        var client = await LoadAsync(model.Id, ct);

        if (client.Status == ClientStatusEnum.Archived && status != ClientStatusEnum.Archived)
            throw new CustomException(HttpStatusCode.Conflict,
                "Un client archive ne peut pas etre reactive en V1.");

        client.Status = status;

        _auditLogService.Record("ClientStatusChanged", nameof(Client), client.Id.ToString(),
            $"Nouveau status : {model.Status}.");
        await _context.SaveChangesAsync(ct);

        return await GetByIdAsync(client.Id, ct);
    }

    public async Task<SuspensionImpactViewModel> GetImpactSuspensionAsync(
        Guid id, CancellationToken ct = default)
    {
        await LoadAsync(id, ct);
        var usage = await _serviceLimitService.GetUsageAsync(id, ct);

        return new SuspensionImpactViewModel
        {
            ChoirCount = usage.Choirs,
            MemberCount = usage.Members
        };
    }

    /// <summary>
    /// Pendant de <see cref="ChangeStatusAsync"/> pour la seule transition Suspendu -> Active
    /// (decision utilisateur, lot 3). Un client Archive ne se reactive jamais (transition
    /// terminale, deja vraie via <see cref="ChangeStatusAsync"/>). Contrairement a une
    /// suspension, une reactivation peut faire ressurgir une consommation qui depasse un
    /// plafond abaisse entre-temps : le refus est explicite et chiffre, sans jamais amputer
    /// l'existant.
    /// </summary>
    public async Task<ClientViewModel> ReactivateAsync(Guid id, CancellationToken ct = default)
    {
        EnsureAdmin();

        var client = await LoadAsync(id, ct);

        if (client.Status == ClientStatusEnum.Archived)
            throw new CustomException(HttpStatusCode.Conflict,
                "Un client archivé ne peut pas être réactivé.");

        if (client.Status == ClientStatusEnum.Active)
            throw new CustomException(HttpStatusCode.Conflict, "Ce client est déjà actif.");

        var usage = await _serviceLimitService.GetUsageAsync(id, ct);
        var depassements = new List<string>();

        if (usage.Choirs > usage.ChoirLimit)
            depassements.Add($"choirs {usage.Choirs}/{usage.ChoirLimit}");
        if (usage.Members > usage.MemberLimit)
            depassements.Add($"members {usage.Members}/{usage.MemberLimit}");
        if (usage.StorageOctets > usage.StorageQuotaBytes)
            depassements.Add($"storage {usage.StorageOctets}/{usage.StorageQuotaBytes} octets");

        if (depassements.Count > 0)
            throw new CustomException(HttpStatusCode.Conflict,
                $"Réactivation impossible, plafond dépassé : {string.Join(", ", depassements)}. "
                + "Ajustez les limites du client avant de réactiver.");

        client.Status = ClientStatusEnum.Active;

        _auditLogService.Record("ClientReactivated", nameof(Client), client.Id.ToString());
        await _context.SaveChangesAsync(ct);

        return await GetByIdAsync(client.Id, ct);
    }

    /// <summary>
    /// Chorales du client, avec leur niveau d'consommation — ecran central de la zone « Ma structure »
    /// (`10-D23`). Un chorale archivee (voir <c>AdminChoirService</c>) n'y figure jamais :
    /// le rattachement client n'ouvre aucun droit sur du contenu masque par l'administration.
    /// </summary>
    public async Task<PagedListViewModel<ClientChoirListItemViewModel>> GetChoirsAsync(
        Guid clientId, PaginateViewModel pagination, CancellationToken ct = default)
    {
        await EnsureClientManagerOrAdminAsync(clientId, ct);

        // Statut != Archive : avant la migration 13, une chorale archivée portait IsDeleted =
        // true et le filtre de requête par défaut suffisait à l'exclure d'ici. Depuis, une
        // chorale archivée reste IsDeleted = false — sans cette exclusion explicite, elle
        // réapparaîtrait dans « Ma structure », alors que son contenu n'est plus censé être
        // accessible au ResponsableClient (`10-D23`).
        var query = _context.Choirs
            .AsNoTracking()
            .Where(c => c.ClientId == clientId && c.Status != ChoirStatusEnum.Archived)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(pagination.Filter))
            query = query.Where(c => c.Name.Contains(pagination.Filter));

        var total = await query.CountAsync(ct);
        var items = await query
            .ApplySort(
                pagination.SortActive, pagination.SortDirection, ClientChoirsSortableColumns, c => c.Id,
                q => q.OrderBy(c => c.Name).ThenBy(c => c.Id))
            .Skip(pagination.Offset)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        var viewModels = _mapper.Map<List<ClientChoirListItemViewModel>>(items);
        await EnrichChoirsAsync(viewModels, ct);

        return new PagedListViewModel<ClientChoirListItemViewModel>
        {
            Items = viewModels,
            TotalCount = total,
            CurrentPage = pagination.Page,
            PageSize = pagination.PageSize
        };
    }

    /// <summary>
    /// Fiche detail d'une chorale du client (`10-D23`). Contrairement a
    /// <see cref="GetChoirsAsync"/>, n'exclut pas <c>Status == Archived</c> : une chorale
    /// archivee doit rester consultable depuis sa fiche pour permettre sa reactivation.
    /// </summary>
    public async Task<ClientChoirDetailViewModel> GetChoirAsync(
        Guid clientId, Guid choirId, CancellationToken ct = default)
    {
        await EnsureClientManagerOrAdminAsync(clientId, ct);

        var choir = await _context.Choirs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == choirId && c.ClientId == clientId, ct)
            ?? throw new KeyNotFoundException($"Choir {choirId} not found.");

        var viewModel = _mapper.Map<ClientChoirDetailViewModel>(choir);

        var aggregates = await ComputeChoirAggregatesAsync([choirId], ct);
        var (memberCount, songCount, upcomingEventCount) = aggregates.GetValueOrDefault(choirId);
        viewModel.MemberCount = memberCount;
        viewModel.SongCount = songCount;
        viewModel.UpcomingEventCount = upcomingEventCount;

        return viewModel;
    }

    /// <summary>
    /// Changement de statut d'une chorale par le ManagerClient de son client (`10-D23`). Meme
    /// table de transitions et memes garde-fous que
    /// <see cref="ChoirServices.AdminChoirService.ChangeStatusAsync"/> : le role client
    /// n'ouvre pas un sous-ensemble restreint de transitions.
    /// </summary>
    public async Task<ClientChoirDetailViewModel> ChangeChoirStatusAsync(
        Guid clientId, Guid choirId, ChoirStatusEnum status, CancellationToken ct = default)
    {
        await EnsureClientManagerOrAdminAsync(clientId, ct);

        // Defense en profondeur : la validation du modele borne deja la plage cote controleur,
        // meme raison que ClientService.ChangeStatusAsync et AdminChoirService.ChangeStatusAsync.
        if (!Enum.IsDefined(status))
            throw new CustomException(HttpStatusCode.BadRequest, "Statut de chorale inconnu.");

        var choir = await _context.Choirs
            .FirstOrDefaultAsync(c => c.Id == choirId && c.ClientId == clientId, ct)
            ?? throw new KeyNotFoundException($"Choir {choirId} not found.");

        if (choir.Status == status)
            return await GetChoirAsync(clientId, choirId, ct);

        if (!ChoirStateHelper.IsTransitionAllowed(choir.Status, status))
            throw new CustomException(HttpStatusCode.Conflict,
                "Transition de statut interdite depuis l'état actuel de la chorale.");

        // Meme garde-fou que AdminChoirService.ChangeStatusAsync : une reactivation depuis
        // Archive doit revalider la place disponible, une chorale Archivee n'occupant pas
        // le plafond (ServiceLimitService.CountChoirsAsync).
        if (status == ChoirStatusEnum.Published && choir.Status == ChoirStatusEnum.Archived)
            await _serviceLimitService.EnsureCanCreateChoirAsync(choir.ClientId, ct);

        choir.Status = status;

        _auditLogService.Record("ClientChoirStatusChanged", nameof(ChoirEntity), choir.Id.ToString(),
            $"Nouveau status : {status}.");
        await _context.SaveChangesAsync(ct);

        return await GetChoirAsync(clientId, choirId, ct);
    }

    public async Task AssignManagerAsync(
        Guid clientId, AssignClientManagerViewModel model, CancellationToken ct = default)
    {
        await LoadAsync(clientId, ct);
        await EnsureClientManagerOrAdminAsync(clientId, ct);

        var user = await _userManager.FindByEmailAsync(model.Email)
            ?? throw new KeyNotFoundException($"Aucun compte pour {model.Email}.");

        var exists = await _context.ClientMembers.AnyAsync(
            m => m.ClientId == clientId
                 && m.UserId == user.Id
                 && m.Role == UserRoleEnum.ClientManager, ct);

        if (exists)
            throw new CustomException(HttpStatusCode.Conflict,
                "Cet utilisateur est deja responsable de ce client.");

        _context.ClientMembers.Add(new ClientMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            ClientId = clientId,
            UserId = user.Id,
            Role = UserRoleEnum.ClientManager,
            IsDeleted = false
        });

        _auditLogService.Record("ClientManagerAssigned", nameof(Client), clientId.ToString(), user.Id);
        await _context.SaveChangesAsync(ct);
    }

    public async Task RemoveManagerAsync(
        Guid clientId, string userId, CancellationToken ct = default)
    {
        // Defense en profondeur : ClientRoleAuthorizationHandler verifie deja le clientId de la
        // route, mais toutes les autres methodes de ce service revalident au niveau service.
        // Laisser ces deux-la sans controle rendait la garantie dependante d'une seule couche.
        await EnsureClientManagerOrAdminAsync(clientId, ct);

        var member = await _context.ClientMembers
            .FirstOrDefaultAsync(m => m.ClientId == clientId
                                      && m.UserId == userId
                                      && m.Role == UserRoleEnum.ClientManager, ct)
            ?? throw new KeyNotFoundException("Manager client not found.");

        // Soft-delete, comme partout ailleurs : l'historique d'audit doit rester lisible.
        member.IsDeleted = true;

        _auditLogService.Record("ClientManagerRemoved", nameof(Client), clientId.ToString(), userId);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<PagedListViewModel<ClientManagerListItemViewModel>> GetManagersAsync(
        Guid clientId, PaginateViewModel pagination, CancellationToken ct = default)
    {
        await EnsureClientManagerOrAdminAsync(clientId, ct);

        // Role filtre explicitement bien que CK_ClientMember_ClientRole garantisse deja que
        // cette table ne porte que des ResponsableClient : meme defense en profondeur que
        // AssignManagerAsync/RemoveManagerAsync, qui filtrent aussi sur le role.
        var query = _context.ClientMembers
            .AsNoTracking()
            .Include(m => m.User)
            .Where(m => m.ClientId == clientId && m.Role == UserRoleEnum.ClientManager);

        if (!string.IsNullOrWhiteSpace(pagination.Filter))
            query = query.Where(m =>
                m.User.Firstname.Contains(pagination.Filter) ||
                m.User.Lastname.Contains(pagination.Filter) ||
                m.User.Email != null && m.User.Email.Contains(pagination.Filter));

        var total = await query.CountAsync(ct);
        var items = await query
            // Tri deterministe par date de designation : sans ThenBy(Id), deux responsables
            // designes a la meme date produiraient une pagination non deterministe.
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Id)
            .Skip(pagination.Offset)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        return new PagedListViewModel<ClientManagerListItemViewModel>
        {
            Items = _mapper.Map<List<ClientManagerListItemViewModel>>(items),
            TotalCount = total,
            CurrentPage = pagination.Page,
            PageSize = pagination.PageSize
        };
    }

    private async Task EnrichUsageAsync(ClientViewModel viewModel, CancellationToken ct)
    {
        if (viewModel.Id is null) return;

        var usage = await _serviceLimitService.GetUsageAsync(viewModel.Id.Value, ct);
        viewModel.ChoirCount = usage.Choirs;
        viewModel.MemberCount = usage.Members;
        viewModel.UsedStorageBytes = usage.StorageOctets;
    }

    /// <summary>
    /// Version groupée pour les lists : la consommation de toute la page en quatre
    /// requêtes, au lieu de quatre requêtes <b>par client</b> — l'enrichissement unitaire
    /// dans une boucle produisait 1 + 4N allers-retours par page.
    /// </summary>
    private async Task EnrichUsagesAsync(List<ClientViewModel> viewModels, CancellationToken ct)
    {
        var ids = viewModels.Where(v => v.Id is not null).Select(v => v.Id!.Value).ToList();
        if (ids.Count == 0) return;

        var choirs = await _context.Choirs
            .Where(c => ids.Contains(c.ClientId))
            .GroupBy(c => c.ClientId)
            .Select(g => new { ClientId = g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.ClientId, x => x.N, ct);

        // Mêmes règles que ServiceLimitService.CountMembersAsync : espace-chorale
        // uniquement, personnes distinctes, archivés exclus.
        var members = (await _context.SpaceMembers
                .Where(m => m.ChoirId != null
                            && m.SpaceId == m.ChoirId
                            && m.Status != MemberStatusEnum.Archived)
                .Join(_context.Choirs.Where(c => ids.Contains(c.ClientId)),
                    m => m.ChoirId, c => c.Id,
                    (m, c) => new { c.ClientId, m.UserId })
                .Distinct()
                .ToListAsync(ct))
            .GroupBy(x => x.ClientId)
            .ToDictionary(g => g.Key, g => g.Count());

        // Soft-deletée inclus, comme dans CalculerStockageAsync : le disque reste occupé.
        var storage = await ComputeStoragePerClientAsync(ids, ct);

        foreach (var viewModel in viewModels)
        {
            if (viewModel.Id is not { } id) continue;
            viewModel.ChoirCount = choirs.GetValueOrDefault(id);
            viewModel.MemberCount = members.GetValueOrDefault(id);
            viewModel.UsedStorageBytes = storage.GetValueOrDefault(id);
        }
    }

    /// <summary>
    /// Stockage consomme par client (partitions + enregistrements), groupe pour toute la liste
    /// d'identifiants recue — extrait de <see cref="EnrichUsagesAsync"/> pour etre
    /// reutilise par <see cref="ComputeClientIdsNearCapAsync"/> sans dupliquer les deux
    /// requetes groupees.
    /// </summary>
    private async Task<Dictionary<Guid, long>> ComputeStoragePerClientAsync(
        List<Guid> clientIds, CancellationToken ct)
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

    /// <summary>
    /// Plus gros fichier unitaire par client (partitions et enregistrements confondus),
    /// compare a <c>MaxFileSizeBytes</c> — quatrieme plafond, distinct du quota global de
    /// <see cref="ComputeStoragePerClientAsync"/>. Meme calcul que
    /// <c>AdminDashboardService.ComputeMaxFileSizePerClientAsync</c>.
    /// </summary>
    private async Task<Dictionary<Guid, long>> ComputeMaxFileSizePerClientAsync(
        List<Guid> clientIds, CancellationToken ct)
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

    /// <summary>
    /// Identifiants des clients a plus de 80% d'au moins un de leurs quatre plafonds (chorales,
    /// membres, stockage, taille de fichier), calcule sur le sous-ensemble deja filtre par
    /// <c>Status</c>/<c>ClientIds</c>/<c>Filter</c> — jamais sur l'ensemble des clients quand un
    /// filtre restreint deja la liste. Meme regle de seuil que
    /// <c>AdminDashboardService.ComputeClientsNearCapAsync</c> : un plafond a 0 est
    /// exclu du calcul (<see cref="ExceedsThreshold"/>), jamais compte a 100%.
    /// </summary>
    private async Task<List<Guid>> ComputeClientIdsNearCapAsync(
        IQueryable<Client> queryCandidates, CancellationToken ct)
    {
        var clients = await queryCandidates
            .Select(c => new
            {
                c.Id,
                c.ChoirLimit,
                c.MemberLimit,
                c.StorageQuotaBytes,
                c.MaxFileSizeBytes
            })
            .ToListAsync(ct);

        if (clients.Count == 0) return [];

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

        return clients
            .Where(c =>
                ExceedsThreshold(choirsByClient.GetValueOrDefault(c.Id), c.ChoirLimit)
                || ExceedsThreshold(membersByClient.GetValueOrDefault(c.Id), c.MemberLimit)
                || ExceedsThreshold(storageByClient.GetValueOrDefault(c.Id), c.StorageQuotaBytes)
                || ExceedsThreshold(maxFileByClient.GetValueOrDefault(c.Id), c.MaxFileSizeBytes))
            .Select(c => c.Id)
            .ToList();
    }

    /// <summary>Un plafond a 0 est exclu du calcul — sinon toute consommation, meme nulle,
    /// se traduirait par un taux de 100%.</summary>
    private static bool ExceedsThreshold(long consumed, long limit)
        => limit > 0 && consumed > limit * NearCapThreshold;

    private async Task<Client> LoadAsync(Guid id, CancellationToken ct)
        => await _context.Clients.FirstOrDefaultAsync(c => c.Id == id, ct)
           ?? throw new KeyNotFoundException($"Client {id} not found.");

    /// <summary>
    /// Reservee a l'administration generale (`10-D23`, decision produit : creation,
    /// modification, plafonds et statut d'un client restent Admin-only). La policy HTTP
    /// restreint deja ces actions, mais un appel direct au service ne doit pas la contourner.
    /// </summary>
    private void EnsureAdmin()
    {
        if (!_currentUserRoles.Contains(UserRoleEnum.Admin))
            throw new CustomException(HttpStatusCode.Forbidden, "Réservé à l'administration générale.");
    }

    /// <summary>
    /// Reservee a l'Admin ou au ResponsableClient du client vise. Refuse en 404 et non en 403
    /// pour un ResponsableClient etranger : meme principe d'occultation que
    /// <c>EventService.GetByIdAsync</c> pour un brouillon — ne pas reveler l'existence d'un
    /// client auquel l'appelant n'a aucun rattachement.
    /// </summary>
    private async Task EnsureClientManagerOrAdminAsync(Guid clientId, CancellationToken ct)
    {
        if (_currentUserRoles.Contains(UserRoleEnum.Admin)) return;

        var roles = await _clientRoleResolverService.ResolveRolesAsync(
            _currentUserId ?? string.Empty, clientId, ct);

        if (!roles.Contains(UserRoleEnum.ClientManager))
            throw new KeyNotFoundException($"Client {clientId} not found.");
    }

    /// <summary>
    /// Version groupée pour les lists : la consommation de toute la page de chorales en trois
    /// requêtes, au lieu de trois requêtes <b>par chorale</b> — même piège et même correctif
    /// que <see cref="EnrichUsagesAsync"/>.
    /// </summary>
    private async Task EnrichChoirsAsync(List<ClientChoirListItemViewModel> viewModels, CancellationToken ct)
    {
        if (viewModels.Count == 0) return;

        var aggregates = await ComputeChoirAggregatesAsync(viewModels.Select(v => v.Id).ToList(), ct);

        foreach (var viewModel in viewModels)
        {
            var (memberCount, songCount, upcomingEventCount) = aggregates.GetValueOrDefault(viewModel.Id);
            viewModel.MemberCount = memberCount;
            viewModel.SongCount = songCount;
            viewModel.UpcomingEventCount = upcomingEventCount;
        }
    }

    /// <summary>
    /// Agregats (membres, chants, evenements a venir) d'un ensemble de chorales, groupes en
    /// trois requetes — extrait de <see cref="EnrichChoirsAsync"/> pour etre reutilise tel
    /// quel par <see cref="GetChoirAsync"/> (fiche detail, une seule chorale) sans dupliquer
    /// ces trois requetes groupees. Comportement de <see cref="GetChoirsAsync"/> inchange.
    /// </summary>
    private async Task<Dictionary<Guid, (int MemberCount, int SongCount, int UpcomingEventCount)>>
        ComputeChoirAggregatesAsync(List<Guid> choirIds, CancellationToken ct)
    {
        if (choirIds.Count == 0) return [];

        var members = (await _context.SpaceMembers
                .Where(m => m.ChoirId != null && choirIds.Contains(m.ChoirId.Value)
                            && m.SpaceId == m.ChoirId && m.Status != MemberStatusEnum.Archived)
                .Select(m => new { ChoirId = m.ChoirId!.Value, m.UserId })
                .Distinct()
                .ToListAsync(ct))
            .GroupBy(x => x.ChoirId)
            .ToDictionary(g => g.Key, g => g.Count());

        var songs = await _context.Songs
            .Where(c => choirIds.Contains(c.ChoirId))
            .GroupBy(c => c.ChoirId)
            .Select(g => new { ChoirId = g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.ChoirId, x => x.N, ct);

        var maintenant = DateTime.UtcNow;
        var eventsUpcoming = await _context.Events
            .Where(e => e.ChoirId != null && choirIds.Contains(e.ChoirId.Value) && (e.EndDate ?? e.StartDate) >= maintenant)
            .GroupBy(e => e.ChoirId!.Value)
            .Select(g => new { ChoirId = g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.ChoirId, x => x.N, ct);

        return choirIds.ToDictionary(
            id => id,
            id => (
                members.GetValueOrDefault(id),
                songs.GetValueOrDefault(id),
                eventsUpcoming.GetValueOrDefault(id)));
    }
}
