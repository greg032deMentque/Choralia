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
using ChoraleBackEnd.Services.UserServices;
using ChoraleBackEnd.ViewModels;
using Microsoft.EntityFrameworkCore;
using ChoraleBackEnd.ViewModels.Events;

namespace ChoraleBackEnd.Services.ChoirServices;

public interface IEventService
{
    Task<PagedListViewModel<EventViewModel>> GetPagedAsync(EventPagedFilterViewModel filter, CancellationToken ct = default);
    Task<EventViewModel> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<EventViewModel> CreateAsync(EventViewModel model, CancellationToken ct = default);
    Task<EventViewModel> UpdateAsync(EventViewModel model, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<EventViewModel> CloseAsync(Guid id, CancellationToken ct = default);
    Task<EventViewModel> ChangeStatusAsync(Guid id, EventStatusEnum status, CancellationToken ct = default);
}

public sealed class EventService : BaseService, IEventService
{
    // Liste blanche de tri : colonnes propres a l'entite, sans jointure — pas d'Include ici
    // sur Chorale/Espace.Client, donc pas de tri sur ces navigations pour cette liste.
    private static readonly IReadOnlyDictionary<string, Expression<Func<Event, object?>>> EventsSortableColumns =
        new Dictionary<string, Expression<Func<Event, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Title"] = e => e.Title,
            ["StartDate"] = e => e.StartDate,
            ["Type"] = e => e.Type,
            ["Status"] = e => e.Status
        };

    private readonly IEventAuthorizationService _authorizationService;
    private readonly IGuestAccountLifecycleService _guestAccountLifecycleService;
    private readonly IClientRoleResolverService _clientRoleResolverService;
    private readonly IMembershipService _membershipService;
    private readonly IEventParticipationSeedingService _eventParticipationSeedingService;

    public EventService(
        IServiceProvider serviceProvider,
        IEventAuthorizationService authorizationService,
        IGuestAccountLifecycleService guestAccountLifecycleService,
        IClientRoleResolverService clientRoleResolverService,
        IMembershipService membershipService,
        IEventParticipationSeedingService eventParticipationSeedingService)
        : base(serviceProvider)
    {
        _authorizationService = authorizationService;
        _guestAccountLifecycleService = guestAccountLifecycleService;
        _clientRoleResolverService = clientRoleResolverService;
        _membershipService = membershipService;
        _eventParticipationSeedingService = eventParticipationSeedingService;
    }

    public async Task<PagedListViewModel<EventViewModel>> GetPagedAsync(
        EventPagedFilterViewModel filter, CancellationToken ct = default)
    {
        var isAdmin = _authorizationService.IsAdmin();

        if (filter.ChoirId.HasValue && !isAdmin)
        {
            var isChoirMember = await _authorizationService.IsMemberChoirActiveAsync(filter.ChoirId.Value, ct);
            if (!isChoirMember)
                throw new CustomException(HttpStatusCode.Forbidden, "Accès réservé aux membres de cette chorale.");
        }

        var query = _context.Events
            .AsNoTracking()
            .AsQueryable();

        if (!isAdmin)
        {
            var allowedSpaceIds = await _context.SpaceMembers
                .AsNoTracking()
                .Where(m => m.UserId == _currentUserId && m.Status == MemberStatusEnum.Active)
                .Select(m => m.SpaceId)
                .ToListAsync(ct);

            query = query.Where(e => allowedSpaceIds.Contains(e.Id));

            // Un Draft est invisible des membres, un Archive est masque par defaut
            // (`04` § Event). Seuls les voient : le gestionnaire de l'espace
            // (Responsable de la chorale ou Organizer de l'evenement) et le createur.
            // Sans ce filtre, les brouillons d'un responsable etaient servis a toute la
            // chorale — et son absence masquait un second defaut : le front ne publiant
            // pas encore, tous les events restaient Draft et n'etaient visibles
            // QUE grace au trou. Les deux corrections partent ensemble.
            var managedSpaces = await _context.SpaceMemberRoles
                .AsNoTracking()
                .Where(r => r.SpaceMember.UserId == _currentUserId
                            && (r.Role == UserRoleEnum.Manager
                                || r.Role == UserRoleEnum.Organizer))
                .Select(r => r.SpaceMember.SpaceId)
                .Distinct()
                .ToListAsync(ct);

            query = query.Where(e =>
                e.Status == EventStatusEnum.Published
                || e.Status == EventStatusEnum.Cancelled
                || managedSpaces.Contains(e.Id)
                || e.CreatedByUserId == _currentUserId);
        }

        if (filter.ChoirId.HasValue)
            query = query.Where(e => e.ChoirId == filter.ChoirId.Value);

        if (filter.Type.HasValue)
            query = query.Where(e => e.Type == filter.Type.Value);

        if (filter.Status.HasValue)
            query = query.Where(e => e.Status == filter.Status.Value);

        if (filter.Upcoming == true)
            query = query.Where(e => (e.EndDate ?? e.StartDate) >= DateTime.UtcNow);
        else if (filter.Upcoming == false)
            query = query.Where(e => (e.EndDate ?? e.StartDate) < DateTime.UtcNow);

        if (!string.IsNullOrWhiteSpace(filter.Filter))
            query = query.Where(e => e.Title.Contains(filter.Filter));

        var total = await query.CountAsync(ct);
        var items = await query
            // Sans OrderBy, Skip/Take produit une pagination non deterministe : lignes
            // dupliquees ou manquantes d'une page a l'autre. ThenBy(Id) departage les
            // events a la meme date. ApplySort applique ce meme tri par defaut quand
            // SortActive est absent ou hors liste blanche (voir TriHelper).
            .ApplySort(
                filter.SortActive, filter.SortDirection, EventsSortableColumns, e => e.Id,
                q => q.OrderBy(e => e.StartDate).ThenBy(e => e.Id))
            .Skip(filter.Offset)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return new PagedListViewModel<EventViewModel>
        {
            Items = _mapper.Map<List<EventViewModel>>(items),
            TotalCount = total,
            CurrentPage = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<EventViewModel> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var evt = await _context.Events
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new KeyNotFoundException($"Event {id} not found.");

        var isAllowed = _authorizationService.IsAdmin()
            || await _authorizationService.IsSpaceMemberAsync(evt.Id, ct);

        if (!isAllowed)
            throw new CustomException(HttpStatusCode.Forbidden, "Accès réservé aux membres de cet événement.");

        // Meme regle qu'en liste : un Draft ou un Archive n'est visible que de son
        // gestionnaire ou de son createur. On repond Introuvable et non Interdit, pour ne
        // pas reveler l'existence d'un contenu non publie (`02` § Regles de visibilite).
        if (evt.Status is EventStatusEnum.Draft or EventStatusEnum.Archived
            && !_authorizationService.IsAdmin()
            && evt.CreatedByUserId != _currentUserId)
        {
            var isManager = await _context.SpaceMemberRoles
                .AsNoTracking()
                .AnyAsync(r => r.SpaceMember.UserId == _currentUserId
                               && r.SpaceMember.SpaceId == evt.Id
                               && (r.Role == UserRoleEnum.Manager
                                   || r.Role == UserRoleEnum.Organizer), ct);

            if (!isManager && evt.ChoirId is { } choirId)
                isManager = await _authorizationService.IsManagerChoirAsync(choirId, ct);

            if (!isManager)
                throw new KeyNotFoundException($"Event {id} not found.");
        }

        return _mapper.Map<EventViewModel>(evt);
    }

    public async Task<EventViewModel> CreateAsync(EventViewModel model, CancellationToken ct = default)
    {
        if (_currentUserId is null)
            throw new CustomException(HttpStatusCode.Unauthorized, "Non authentifié.");

        // Bloquant pour CREER un espace (lot 6), comme ChoirService.CreateAsync. Aucun
        // bypass Admin.
        var currentUser = await GetCurrentUserEntityAsync(ct);

        if (currentUser is not null && !currentUser.EmailConfirmed)
            throw new CustomException(HttpStatusCode.Forbidden,
                "Vérifiez votre adresse email avant de créer un événement.");

        // Client de rattachement de l'espace, dans cet ordre (`10-D23`) : herite de la
        // chorale porteuse si l'evenement en a une ; sinon celui explicitement designe ;
        // sinon celui du createur lui-meme, qui est necessairement un client (decision
        // produit : « la personne qui cree un evenement autonome est elle aussi un
        // client »). Un evenement sans client n'existe pas — Guid.Empty ne doit jamais
        // pouvoir atteindre Espace.ClientId, sous peine de reproduire exactement le trou
        // que ce lot devait fermer (violation de FK en base reelle, ou espace hors quota si
        // la FK venait a manquer).
        Guid clientId;

        if (model.ChoirId.HasValue)
        {
            var choir = await LoadChoirAsync(model.ChoirId.Value, ct);
            await _authorizationService.EnsureManagerChoirAsync(model.ChoirId.Value, ct);
            clientId = choir.ClientId;
        }
        else if (model.ClientId.HasValue)
        {
            await EnsureClientManagerOuAdminAsync(model.ClientId.Value, ct);
            clientId = model.ClientId.Value;
        }
        else
        {
            clientId = await ResolveCreatorClientAsync(ct);
        }

        var evt = _mapper.Map<Event>(model);
        evt.Id = ChoraleDbContext.NewIdGuid();
        // ChoirId est ignore par le profil AutoMapper (rattachement fige, decision produit) :
        // pose ici explicitement, seul point ou il est legitimement affecte.
        evt.ChoirId = model.ChoirId;

        _context.Spaces.Add(new Space
        {
            Id = evt.Id,
            SpaceType = SpaceTypeEnum.Event,
            EndDate = evt.EndDate,
            ClientId = clientId,
            IsDeleted = false
        });

        _context.Events.Add(evt);

        var creatorMember = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = _currentUserId,
            SpaceId = evt.Id,
            ChoirId = model.ChoirId,
            Status = MemberStatusEnum.Active,
            Presence = AttendanceEnum.NoReply,
            IsDeleted = false
        };
        _context.SpaceMembers.Add(creatorMember);

        // D39 (`10-decisions.md`) : le role Organizer n'est affecte qu'aux evenements
        // autonomes (ChoirId nul). Un evenement rattache a une chorale est deja gere par
        // les Manager de cette chorale (EnsureEventManagerAsync) — y ajouter un
        // Organizer creerait deux chemins d'autorite concurrents sur le meme espace, sans
        // regle pour les departager. Le createur d'un evenement de chorale y arrive
        // necessairement deja Manager (EnsureManagerChoirAsync ci-dessus), donc dispose
        // deja de toutes les capacites d'un Organizer sans qu'on ait besoin de le lui
        // affecter en plus.
        if (!model.ChoirId.HasValue)
        {
            _authorizationService.EnsureOrganizerAssignable(model.ChoirId);
            _context.SpaceMemberRoles.Add(new SpaceMemberRole
            {
                Id = ChoraleDbContext.NewIdGuid(),
                SpaceMemberId = creatorMember.Id,
                Role = UserRoleEnum.Organizer
            });
        }

        await _context.SaveChangesAsync(ct);
        return _mapper.Map<EventViewModel>(evt);
    }

    public async Task<EventViewModel> UpdateAsync(EventViewModel model, CancellationToken ct = default)
    {
        if (model.Id is null)
            throw new CustomException(HttpStatusCode.BadRequest, "Id requis.");

        var evt = await _context.Events
            .FirstOrDefaultAsync(e => e.Id == model.Id, ct)
            ?? throw new KeyNotFoundException($"Event {model.Id} not found.");

        await _authorizationService.EnsureEventManagerAsync(evt, ct);

        // Ferme la modification si la chorale ACTUELLE de l'evenement n'accepte plus
        // l'ecriture (Archive/Annule). Un evenement autonome (ChoirId nul) n'est pas concerne.
        await EnsureWriteChoirAsync(evt.ChoirId, ct);

        // Rattachement fige (decision produit) : ChoirId se decide exclusivement a la
        // creation et ne bouge plus jamais ensuite — ni pour rattacher un evenement
        // autonome a une chorale, ni pour le deplacer vers une autre. Sans cette garde, un
        // Organizer affecte a la creation d'un evenement autonome (D39) deviendrait invalide
        // des lors que l'evenement rejoindrait une chorale. Defense en profondeur : le
        // profil AutoMapper ignore deja ChoirId cote mapping (voir EventViewModel), cette
        // garde couvre le cas ou le champ serait un jour remappe par erreur.
        if (EventStateHelper.IsChoirIdChangeRequested(evt.ChoirId, model.ChoirId))
            throw new CustomException(
                HttpStatusCode.BadRequest,
                "Le assignment d'un événement à une chorale se décide à sa création : "
                + "il ne peut plus être ajouté ni modifié par la suite.");

        _mapper.Map(model, evt);

        // Espace.EndDate doit suivre Event.EndDate. Sans cette resynchronisation, la
        // date scope par l'espace se fige a la creation : tout ce qui raisonne sur l'espace
        // — duree de vie, purge, tri — travaille alors sur une date perimee, sans qu'aucune
        // erreur ne soit levee.
        await ResyncSpaceEndDateAsync(evt, ct);

        await _context.SaveChangesAsync(ct);
        return _mapper.Map<EventViewModel>(evt);
    }

    public async Task<EventViewModel> ChangeStatusAsync(
        Guid id, EventStatusEnum status, CancellationToken ct = default)
    {
        var evt = await _context.Events
            .FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new KeyNotFoundException($"Event {id} not found.");

        await _authorizationService.EnsureEventManagerAsync(evt, ct);
        await EnsureWriteChoirAsync(evt.ChoirId, ct);

        if (evt.Status == status)
            return _mapper.Map<EventViewModel>(evt);

        if (!EventStateHelper.IsTransitionAllowed(evt.Status, status))
            throw new CustomException(HttpStatusCode.Conflict,
                "Transition de statut interdite depuis l'état actuel de l'événement.");

        // Un evenement se publie avec un lieu : sans lieu, il n'est pas actionnable pour un
        // participant (`04` § Event).
        if (status == EventStatusEnum.Published && string.IsNullOrWhiteSpace(evt.Location))
            throw new CustomException(HttpStatusCode.BadRequest,
                "Le lieu est requis pour publier un événement.");

        evt.Status = status;

        // Regle unique (`04` § Membre/Event) : « un membre actif d'une chorale est participant
        // des evenements publies a venir de cette chorale ». Un evenement autonome (ChoirId
        // nul) n'est jamais concerne.
        if (status == EventStatusEnum.Published && evt.ChoirId is { } choirId)
            await _eventParticipationSeedingService.SeedForPublishedEventAsync(evt.Id, choirId, ct);

        await _context.SaveChangesAsync(ct);
        return _mapper.Map<EventViewModel>(evt);
    }

    private async Task ResyncSpaceEndDateAsync(Event evt, CancellationToken ct)
    {
        var space = await _context.Spaces.FirstOrDefaultAsync(e => e.Id == evt.Id, ct);
        if (space is null) return;

        var expectedEndDate = evt.EndDate ?? evt.StartDate;
        if (space.EndDate != expectedEndDate)
            space.EndDate = expectedEndDate;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var evt = await _context.Events
            .Include(e => e.Space)
            .FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new KeyNotFoundException($"Event {id} not found.");

        await _authorizationService.EnsureEventManagerAsync(evt, ct);
        await EnsureWriteChoirAsync(evt.ChoirId, ct);

        evt.IsDeleted = true;
        evt.Space.IsDeleted = true;
        await _context.SaveChangesAsync(ct);
    }

    public async Task<EventViewModel> CloseAsync(Guid id, CancellationToken ct = default)
    {
        var evt = await _context.Events
            .FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new KeyNotFoundException($"Event {id} not found.");

        await _authorizationService.EnsureEventManagerAsync(evt, ct);
        await EnsureWriteChoirAsync(evt.ChoirId, ct);

        if (evt.ClosedAt is not null)
            throw new CustomException(
                "Event déjà clôturé.",
                "Cet événement est déjà clôturé.",
                HttpStatusCode.Conflict);

        if (!EventStateHelper.IsFinished(evt.StartDate, evt.EndDate))
            throw new CustomException(
                HttpStatusCode.BadRequest,
                "Impossible de clôturer : l'événement n'est pas terminé.");

        evt.ClosedAt = DateTime.UtcNow;
        await _guestAccountLifecycleService.AnonymizeUnclaimedGuestsForSpaceAsync(evt.Id, ct);

        await _context.SaveChangesAsync(ct);
        return _mapper.Map<EventViewModel>(evt);
    }

    /// <summary>
    /// Charge la chorale de rattachement d'un evenement — a la creation UNIQUEMENT :
    /// ChoirId est fige ensuite (rattachement fige, decision produit), il n'existe plus de
    /// chemin de rattachement ou de deplacement en modification.
    /// </summary>
    /// <remarks>
    /// Avant la migration 13, une chorale archivee posait <c>IsDeleted</c> : la FK ne pouvait
    /// pas la cibler, et ce garde-fou etait implicite. Le nouveau mecanisme de statut a
    /// retire cet effet de bord — <c>IsDeleted</c> reste desormais a <c>false</c> pour une
    /// chorale <c>Archive</c> — sans qu'aucun controle explicite ne prenne le relais : un
    /// evenement pouvait se rattacher a une chorale <c>Archive</c> ou <c>Annule</c>. Seule une
    /// chorale <c>Publie</c> peut recevoir un evenement : <c>Draft</c> n'a pas encore ses
    /// pupitres ni ses membres, et <c>Archive</c>/<c>Annule</c> ne doivent plus accueillir de
    /// nouveau content.
    /// </remarks>
    private async Task<Data.Entities.Choir> LoadChoirAsync(Guid choirId, CancellationToken ct)
    {
        var choir = await _context.Choirs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == choirId, ct)
            ?? throw new CustomException(HttpStatusCode.NotFound, "Chorale introuvable.");

        if (choir.Status != ChoirStatusEnum.Published)
            throw new CustomException(HttpStatusCode.Conflict,
                "Impossible de rattacher un événement à cette chorale : "
                + "seule une chorale publiée peut recevoir un événement.");

        return choir;
    }

    /// <summary>
    /// Ferme l'ecriture sur un evenement DEJA rattache a une chorale, des lors que cette
    /// chorale n'accepte plus le contenu — voir
    /// <see cref="IMembershipService.EnsureCanWriteAsync"/>. Contrairement a
    /// <see cref="LoadChoirAsync"/> (strictement <c>Publie</c>, uniquement a la creation),
    /// ce garde tolere aussi <c>Draft</c> : un evenement deja rattache reste modifiable
    /// pendant la preparation de sa chorale. Un evenement autonome (sans chorale) n'est pas
    /// concerne.
    /// </summary>
    private async Task EnsureWriteChoirAsync(Guid? choirId, CancellationToken ct)
    {
        if (choirId.HasValue)
            await _membershipService.EnsureCanWriteAsync(choirId.Value, ct);
    }

    /// <summary>
    /// Reservee a l'Admin ou au ResponsableClient du client vise. Ne s'applique qu'aux
    /// events autonomes qui designent explicitement un client (`10-D23`) — un evenement
    /// rattache a une chorale herite deja du controle exerce sur cette chorale.
    /// </summary>
    private async Task EnsureClientManagerOuAdminAsync(Guid clientId, CancellationToken ct)
    {
        // Le client doit etre actif que l'appelant soit Admin ou non : create du contenu
        // nouveau pour un client suspendu ou archive n'est jamais permis (`10-D23`), a la
        // difference de la LECTURE en mode support qui reste ouverte a l'Admin.
        await LoadClientActiveAsync(clientId, ct);

        if (_authorizationService.IsAdmin()) return;

        var roles = await _clientRoleResolverService.ResolveRolesAsync(_currentUserId ?? string.Empty, clientId, ct);
        if (!roles.Contains(UserRoleEnum.ClientManager))
            throw new CustomException(HttpStatusCode.Forbidden, "Vous n'êtes pas responsable de ce client.");
    }

    /// <summary>
    /// Resout le client de rattachement d'un evenement autonome depuis l'identite de son
    /// createur, quand ni chorale ni client ne sont fournis explicitement. Decision produit :
    /// « la personne qui cree un evenement autonome est elle aussi un client » — en pratique,
    /// son (ou ses) rattachement(s) <see cref="ClientMember"/> en tant que ResponsableClient.
    /// </summary>
    /// <remarks>
    /// Ne choisit jamais arbitrairement : plusieurs clients ou aucun client sont tous deux
    /// des refus explicites, jamais une valeur par defaut silencieuse.
    /// </remarks>
    private async Task<Guid> ResolveCreatorClientAsync(CancellationToken ct)
    {
        var clientIds = await _context.ClientMembers
            .AsNoTracking()
            .Where(m => m.UserId == _currentUserId && !m.IsDeleted && m.Role == UserRoleEnum.ClientManager)
            .Select(m => m.ClientId)
            .Distinct()
            .ToListAsync(ct);

        if (clientIds.Count == 0)
            throw new CustomException(
                HttpStatusCode.BadRequest,
                "Aucun client de assignment pour créer un événement autonome. "
                + "Contactez l'administration pour en créer un.");

        if (clientIds.Count > 1)
            throw new CustomException(
                HttpStatusCode.BadRequest,
                "Vous êtes rattaché à plusieurs clients : précisez ClientId.");

        await LoadClientActiveAsync(clientIds[0], ct);
        return clientIds[0];
    }

    private async Task<Client> LoadClientActiveAsync(Guid clientId, CancellationToken ct)
    {
        var client = await _context.Clients
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == clientId, ct)
            ?? throw new CustomException(HttpStatusCode.NotFound, "Client introuvable.");

        if (client.Status != ClientStatusEnum.Active)
            throw new CustomException(HttpStatusCode.Forbidden, "Ce client n'est pas actif : aucune écriture n'est autorisée.");

        return client;
    }
}
