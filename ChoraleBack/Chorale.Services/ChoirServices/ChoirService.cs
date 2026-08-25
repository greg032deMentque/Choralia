using System.Net;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.ViewModels;
using Microsoft.EntityFrameworkCore;
using ChoraleBackEnd.ViewModels.Choirs;

namespace ChoraleBackEnd.Services.ChoirServices;

public interface IChoirService
{
    Task<PagedListViewModel<ChoirViewModel>> GetPagedAsync(PaginateViewModel pagination, CancellationToken ct = default);
    Task<ChoirViewModel> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ChoirViewModel> CreateAsync(ChoirViewModel model, CancellationToken ct = default);
    Task<ChoirViewModel> UpdateAsync(ChoirViewModel model, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task AddMemberAsync(Guid choirId, string userId, CancellationToken ct = default);
    Task RemoveMemberAsync(Guid choirId, string userId, CancellationToken ct = default);
}

public sealed class ChoirService : BaseService, IChoirService
{
    private readonly IAuditLogService _auditLogService;
    private readonly IServiceLimitService _serviceLimitService;

    private readonly IMembershipService _membershipService;
    private readonly IClientRoleResolverService _clientRoleResolverService;
    private readonly ISpaceRoleResolverService _spaceRoleResolverService;
    private readonly ISectionService _sectionService;

    public ChoirService(
        IServiceProvider serviceProvider,
        IAuditLogService auditLogService,
        IServiceLimitService serviceLimitService,
        IMembershipService membershipService,
        IClientRoleResolverService clientRoleResolverService,
        ISpaceRoleResolverService spaceRoleResolverService,
        ISectionService sectionService)
        : base(serviceProvider)
    {
        _auditLogService = auditLogService;
        _serviceLimitService = serviceLimitService;
        _membershipService = membershipService;
        _clientRoleResolverService = clientRoleResolverService;
        _spaceRoleResolverService = spaceRoleResolverService;
        _sectionService = sectionService;
    }

    public async Task<PagedListViewModel<ChoirViewModel>> GetPagedAsync(
        PaginateViewModel pagination, CancellationToken ct = default)
    {
        var isAdmin = _currentUserRoles.Contains(UserRoleEnum.Admin);

        var query = _context.Choirs
            .AsNoTracking()
            .AsQueryable();

        if (!isAdmin)
        {
            // Deux sources d'acces, toutes deux derivees de l'identite authentifiee :
            // l'appartenance (chanteur, chef de pupitre, responsable de la chorale), et le
            // role ResponsableClient, qui donne visibilite sur toutes les chorales de son
            // client meme sans y etre personnellement membre.
            var accessibles = await _membershipService.ChoirsAccessibleAsync(ct);
            var clientIds = await _clientRoleResolverService.ResolveClientIdsAsync(
                _currentUserId ?? string.Empty, ct);
            query = query.Where(c => accessibles.Contains(c.Id) || clientIds.Contains(c.ClientId));
        }

        if (!string.IsNullOrWhiteSpace(pagination.Filter))
            query = query.Where(c => c.Name.Contains(pagination.Filter));

        var total = await query.CountAsync(ct);
        var items = await query
            // Sans OrderBy, Skip/Take produit une pagination non deterministe : lignes
            // dupliquees ou manquantes d'une page a l'autre. ThenBy(Id) departage les
            // chorales de meme nom.
            .OrderBy(c => c.Name)
            .ThenBy(c => c.Id)
            .Skip(pagination.Offset)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        return new PagedListViewModel<ChoirViewModel>
        {
            Items = _mapper.Map<List<ChoirViewModel>>(items),
            TotalCount = total,
            CurrentPage = pagination.Page,
            PageSize = pagination.PageSize
        };
    }

    public async Task<ChoirViewModel> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var isAdmin = _currentUserRoles.Contains(UserRoleEnum.Admin);

        var choir = await _context.Choirs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException($"Choir {id} not found.");

        if (!isAdmin)
        {
            var isMember = await _membershipService.IsMemberActiveAsync(id, ct);
            if (!isMember)
            {
                var clientIds = await _clientRoleResolverService.ResolveClientIdsAsync(
                    _currentUserId ?? string.Empty, ct);
                if (!clientIds.Contains(choir.ClientId))
                    throw new CustomException(HttpStatusCode.Forbidden, "Accès refusé à cette chorale.");
            }
        }

        return _mapper.Map<ChoirViewModel>(choir);
    }

    public async Task<ChoirViewModel> CreateAsync(ChoirViewModel model, CancellationToken ct = default)
    {
        // Bloquant pour CREER un espace, comme toute creation hors auto-service (lot 6) : un
        // compte non verifie ne doit pouvoir produire ni chorale, ni pupitres, ni quota.
        await EnsureEmailConfirmedAsync(ct);

        if (model.ClientId == Guid.Empty)
            throw new CustomException(HttpStatusCode.BadRequest,
                "ClientId requis : il n'exists pas de chorale sans client.");

        if (string.IsNullOrWhiteSpace(model.ChoirMasterEmail))
            throw new CustomException(HttpStatusCode.BadRequest,
                "L'email du chef de chœur est requis à la création : une chorale sans chef "
                + "de chœur ne peut être gérée par personne (aucune porte d'entrée n'accepte "
                + "un appelant qui n'est pas déjà membre de la chorale).");

        // C'est desormais le ResponsableClient qui cree ses chorales, plus l'administration
        // generale (`10-D23`). La policy HTTP est volontairement plus large (Admin OU
        // ResponsableClient) : cette verification est ce qui garantit qu'un responsable ne
        // peut create que dans SON client, pas dans celui d'un autre.
        await EnsureManagerDuClientAsync(model.ClientId, ct);

        // Le plafond est verifie ici, au point d'ecriture. Le verifier a l'ecran seulement
        // le rendrait contournable par appel direct. Verifie AVANT la resolution du compte
        // (etape suivante) : une creation vouee a l'echec par plafond ne doit pas reveler si
        // l'email existe.
        await _serviceLimitService.EnsureCanCreateChoirAsync(model.ClientId, ct);

        var choirMaster = await _userManager.FindByEmailAsync(model.ChoirMasterEmail)
            ?? throw new KeyNotFoundException($"Aucun compte pour {model.ChoirMasterEmail}.");

        // Meme plafond que EnsureCanAddMemberAsync, mais appelable avant que le Space
        // n'existe en base (voir ServiceLimitService.EnsureCanAddMemberToNewChoirAsync).
        await _serviceLimitService.EnsureCanAddMemberToNewChoirAsync(model.ClientId, ct);

        var choir = _mapper.Map<Data.Entities.Choir>(model);
        choir.Id = ChoraleDbContext.NewIdGuid();
        choir.ClientId = model.ClientId;

        // Publie des la creation (`10-Q22`) : le parcours d'inscription auto-service qui
        // rendra Draft utile n'existe pas encore, et une chorale creee invisible de ses
        // propres membres casserait le parcours actuel.
        choir.Status = ChoirStatusEnum.Published;

        _context.Spaces.Add(new Space
        {
            Id = choir.Id,
            SpaceType = SpaceTypeEnum.Choir,
            ClientId = model.ClientId,
            EndDate = null,
            IsDeleted = false
        });

        _context.Choirs.Add(choir);

        foreach (var voicePart in Enum.GetValues<VoicePartEnum>())
        {
            _context.Sections.Add(new Section
            {
                Id = ChoraleDbContext.NewIdGuid(),
                ChoirId = choir.Id,
                VoicePart = voicePart
            });
        }

        // Amorce le premier chef de chœur : sans lui, ChoirMembersController, AddMember et
        // SpaceJoinCode restent tous fermes (policy SpaceManager/ChoirManager, aucun bypass
        // ClientManager) et la chorale serait definitivement ingerable par personne. Le
        // ResponsableClient createur ne devient PAS lui-meme membre (`10-D23`) : seul le
        // compte designe ici l'est.
        var spaceMember = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            ChoirId = choir.Id,
            SpaceId = choir.Id,
            UserId = choirMaster.Id,
            Status = MemberStatusEnum.Active,
            IsDeleted = false
        };
        _context.SpaceMembers.Add(spaceMember);
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SpaceMemberId = spaceMember.Id,
            Role = UserRoleEnum.Manager
        });

        _auditLogService.Record("ChoirCreated", "Choir", choir.Id.ToString());
        _auditLogService.Record("ChoirMasterAssigned", nameof(SpaceMember), spaceMember.Id.ToString(), choirMaster.Id);
        await _context.SaveChangesAsync(ct);
        return _mapper.Map<ChoirViewModel>(choir);
    }

    public async Task<ChoirViewModel> UpdateAsync(ChoirViewModel model, CancellationToken ct = default)
    {
        if (model.Id is null)
            throw new CustomException(HttpStatusCode.BadRequest, "Id requis.");

        var choir = await _context.Choirs
            .Include(c => c.Space)
            .FirstOrDefaultAsync(c => c.Id == model.Id, ct)
            ?? throw new KeyNotFoundException($"Choir {model.Id} not found.");

        // Le client d'appartenance se deduit de la ressource EXISTANTE (chorale.ClientId,
        // charge ci-dessus depuis la base), jamais de ce que l'appelant declare dans le
        // corps : sinon un ResponsableClient pourrait update la chorale d'un autre client
        // en indiquant le sien. C'est aussi ce que verifie desormais la policy HTTP
        // (`ClientRoleAuthorizationHandler`) — les deux lisent la meme valeur, celle de la
        // ressource.
        await EnsureManagerDuClientAsync(choir.ClientId, ct);

        // ChoirViewModel.ClientId est ignore par ce mapping (voir le profil AutoMapper) :
        // Update ne peut donc jamais deplacer une chorale vers un autre client, quelle que
        // soit la valeur envoyee dans le corps.
        _mapper.Map(model, choir);

        await _context.SaveChangesAsync(ct);
        return _mapper.Map<ChoirViewModel>(choir);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var choir = await _context.Choirs
            .Include(c => c.Space)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException($"Choir {id} not found.");

        // Meme regle qu'Update : le client se deduit de la chorale ciblee, jamais d'une
        // valeur declaree par l'appelant (Delete ne porte d'ailleurs aucun corps).
        await EnsureManagerDuClientAsync(choir.ClientId, ct);

        choir.IsDeleted = true;
        choir.Space.IsDeleted = true;
        await _context.SaveChangesAsync(ct);
    }

    public async Task AddMemberAsync(Guid choirId, string userId, CancellationToken ct = default)
    {
        // Meme ordre que SongService (EnsureMemberActiveAsync -> EnsureCanWriteAsync ->
        // role) : l'appartenance de l'APPELANT est verifiee en premier (403 s'il n'est meme
        // pas membre actif, y compris si sa PROPRE appartenance est archivee), puis la
        // fermeture d'ecriture de la chorale (409 si Annule/Archive alors que l'appelant est
        // bien membre actif), puis enfin le role Responsable (403).
        await _membershipService.EnsureMemberActiveAsync(choirId, ct);
        await _membershipService.EnsureCanWriteAsync(choirId, ct);
        await EnsureManagerChoirNoAdminBypassAsync(choirId, ct);

        var exists = await _context.SpaceMembers
            .AnyAsync(m => m.ChoirId == choirId && m.UserId == userId, ct);

        if (exists) return;

        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            ChoirId = choirId,
            SpaceId = choirId,
            UserId = userId,
            Status = MemberStatusEnum.Active,
            IsDeleted = false
        });
        await _context.SaveChangesAsync(ct);
    }

    public async Task RemoveMemberAsync(Guid choirId, string userId, CancellationToken ct = default)
    {
        await _membershipService.EnsureMemberActiveAsync(choirId, ct);
        await _membershipService.EnsureCanWriteAsync(choirId, ct);
        await EnsureManagerChoirNoAdminBypassAsync(choirId, ct);

        var member = await _context.SpaceMembers
            .FirstOrDefaultAsync(m => m.ChoirId == choirId && m.UserId == userId, ct)
            ?? throw new KeyNotFoundException("Member not found in this choir.");

        var isSectionLeader = await _sectionService.IsSectionLeaderInChoirAsync(choirId, userId, ct);

        if (isSectionLeader)
            throw new CustomException(
                HttpStatusCode.BadRequest,
                "Retirez d'abord le rôle de chef de pupitre avant de retirer ce membre.");

        // Point unique de l'invariant "au moins un Manager actif" — voir
        // IMembershipService.EnsureNotLastManagerAsync, partagee avec ChoirMembersService et
        // ChoirMasterService. Verifiee seulement si le membre retire est effectivement
        // Manager : les autres retraits ne touchent pas ce decompte.
        var isManager = await _context.SpaceMemberRoles
            .AnyAsync(r => r.SpaceMemberId == member.Id && r.Role == UserRoleEnum.Manager, ct);
        if (isManager)
            await _membershipService.EnsureNotLastManagerAsync(choirId, member.Id, ct);

        member.IsDeleted = true;
        member.Status = MemberStatusEnum.Archived;

        _auditLogService.Record("MemberRemovedFromChoir", nameof(SpaceMember), member.Id.ToString());
        await _context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Bloquant pour create du contenu (lot 6) — pas pour rejoindre. Aucun bypass Admin : la
    /// decision produit ne prevoit pas d'exception de role.
    /// </summary>
    private async Task EnsureEmailConfirmedAsync(CancellationToken ct)
    {
        var currentUser = await GetCurrentUserEntityAsync(ct);

        if (currentUser is not null && !currentUser.EmailConfirmed)
            throw new CustomException(HttpStatusCode.Forbidden,
                "Vérifiez votre adresse email avant de créer une chorale.");
    }

    /// <summary>
    /// Reservee au Responsable de la chorale. L'Admin n'a pas de bypass ici : il a acces en
    /// lecture a tout, mais aucune ecriture de contenu (`10-D23`, decision produit).
    /// </summary>
    private async Task EnsureManagerChoirNoAdminBypassAsync(Guid choirId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_currentUserId))
            throw new CustomException(HttpStatusCode.Forbidden, "Action réservée à un chef de chœur de cette chorale.");

        var rolesBySpace = await _spaceRoleResolverService.ResolveRolesAsync(_currentUserId, [choirId], ct);

        if (rolesBySpace.TryGetValue(choirId, out var roles) && roles.Contains(UserRoleEnum.Manager))
            return;

        throw new CustomException(HttpStatusCode.Forbidden, "Action réservée à un chef de chœur de cette chorale.");
    }

    /// <summary>
    /// Reservee a l'Admin ou au ResponsableClient du client indique. Partagee par
    /// Create/Update/Delete : dans les trois cas, <paramref name="clientId"/> doit toujours
    /// venir de la ressource (existante pour Update/Delete, du modele valide pour Create),
    /// jamais d'une valeur que l'appelant pourrait usurper.
    /// </summary>
    private async Task EnsureManagerDuClientAsync(Guid clientId, CancellationToken ct)
    {
        if (_currentUserRoles.Contains(UserRoleEnum.Admin)) return;

        var roles = await _clientRoleResolverService.ResolveRolesAsync(
            _currentUserId ?? string.Empty, clientId, ct);
        if (!roles.Contains(UserRoleEnum.ClientManager))
            throw new CustomException(HttpStatusCode.Forbidden, "Vous n'êtes pas responsable de ce client.");
    }
}
