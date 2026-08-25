using System.Net;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Services;
using Microsoft.EntityFrameworkCore;

namespace ChoraleBackEnd.Services.AuthServices;

/// <summary>
/// Source unique de vérité pour « cet utilisateur a-t-il accès au contenu de cette
/// choir ».
/// </summary>
/// <remarks>
/// Ce contrôle existait en sept exemplaires — <c>SongService</c>, <c>ScoreService</c>,
/// <c>RecordingService</c>, <c>SongListService</c>, <c>InstructionService</c>,
/// <c>ChoirService</c>, <c>DashboardService</c> — et ils <b>divergeaient</b> : seul
/// <c>DashboardService</c> exigeait un statut actif, aucun ne regardait le client. Une règle
/// d'autorisation en sept copies est une règle qu'on ne peut pas faire évoluer.
///
/// Conditions de base, toutes nécessaires :
/// <list type="number">
/// <item>appartenance à la chorale ;</item>
/// <item>statut <c>Active</c> — <c>04</c> § Membre pose « inactive : accès révoqué » et
/// « archivé : accès révoqué ». <c>Invite</c> désigne un compte créé avant sa première
/// connexion : il ne lit pas de contenu ;</item>
/// <item>client de la chorale <c>Active</c> — sans quoi la suspension d'un client
/// (<c>10-D23</c>) ne bloquerait que les routes portant une policy scopée, laissant toute la
/// lecture de contenu ouverte.</item>
/// </list>
///
/// Depuis la migration 13, le <c>Status</c> de la chorale s'ajoute à ces conditions
/// (`04` § Choir) :
/// <list type="bullet">
/// <item><c>Publie</c> et <c>Annule</c> : lecture accordée à tout membre actif — un
/// événement annulé reste visible, seule son écriture est fermée (<see cref="CanWriteAsync"/>) ;</item>
/// <item><c>Draft</c> : accordée au seul créateur (<c>CreatedByUserId</c>) et aux
/// <c>Manager</c> de la chorale, jamais à un membre simple — la chorale n'a pas encore
/// ses pupitres ni ses membres définis ;</item>
/// <item><c>Archive</c> : refusée à tout le monde sauf l'Admin, exactement comme avant ce
/// lot (c'est ce statut qui reprend le rôle jusque-là porté par <c>IsDeleted</c>).</item>
/// </list>
/// </remarks>
public interface IMembershipService
{
    Task<bool> IsMemberActiveAsync(Guid choirId, CancellationToken ct = default);

    /// <summary>Lève <c>403</c> si l'accès n'est pas accordé.</summary>
    Task EnsureMemberActiveAsync(Guid choirId, CancellationToken ct = default);

    /// <summary>
    /// Chorales dont l'utilisateur courant peut lire le contenu. Destinée aux requêtes de
    /// liste, qui doivent restreindre <b>inconditionnellement</b> avant tout filtre fourni
    /// par l'appelant.
    /// </summary>
    Task<List<Guid>> ChoirsAccessibleAsync(CancellationToken ct = default);

    /// <summary>
    /// Écriture de contenu autorisée : lecture accordée ET chorale <c>Publie</c> ou
    /// <c>Draft</c>. Le <c>Draft</c> est le statut de préparation — son créateur et
    /// les <c>Manager</c> y écrivent déjà via <see cref="IsMemberActiveAsync"/>, qui
    /// réserve ce statut à ces deux profils ; cette méthode ne fait que confirmer ce que la
    /// lecture a déjà tranché, elle n'ouvre aucun accès supplémentaire. Une chorale
    /// <c>Annule</c> reste lisible (<see cref="IsMemberActiveAsync"/>) mais son contenu passe
    /// en lecture seule — c'est cette méthode qui ferme l'écriture, y compris pour l'Admin.
    /// </summary>
    Task<bool> CanWriteAsync(Guid choirId, CancellationToken ct = default);

    /// <summary>Lève <c>409</c> si l'écriture n'est pas autorisée.</summary>
    Task EnsureCanWriteAsync(Guid choirId, CancellationToken ct = default);

    /// <summary>
    /// Ne verifie QUE le statut de la chorale (<c>Publie</c> ou <c>Draft</c>), independamment
    /// de l'appartenance de l'appelant — a la difference de <see cref="EnsureCanWriteAsync"/>,
    /// qui exige en plus que l'appelant soit lui-meme membre actif. Destinee aux actions
    /// portees par un role scope CLIENT (ex. <c>ChoirMasterService</c>), ou l'appelant n'est
    /// justement pas necessairement membre de la chorale visee. Leve <c>409</c> si la
    /// chorale n'accepte plus l'ecriture (statut <c>Annule</c> ou <c>Archive</c>).
    /// </summary>
    Task EnsureChoirAcceptsWriteAsync(Guid choirId, CancellationToken ct = default);

    /// <summary>
    /// Leve <c>409</c> si <paramref name="spaceMemberId"/> est le DERNIER <c>Manager</c> actif
    /// de la chorale visee. Invariant partage par toute porte qui peut retirer/retrograder/
    /// archiver/desactiver un Manager — <c>ChoirMasterService.RevokeAsync</c>,
    /// <c>ChoirMembersService.RevokeManagerRoleAsync</c>,
    /// <c>ChoirMembersService.ArchiveMemberAsync</c>,
    /// <c>ChoirMembersService.ChangeStatusAsync</c> (branche <c>Inactive</c>),
    /// <c>ChoirService.RemoveMemberAsync</c> — aucune ne doit pouvoir laisser une chorale sans
    /// aucun chef de chœur actif : les trois portes d'entree qui permettent ensuite de la
    /// repeupler (<c>ChoirMembersController</c>, <c>ChoirController.AddMember</c>,
    /// <c>SpaceJoinCodeController</c>) exigent toutes une appartenance active prealable.
    /// Compte les AUTRES Managers actifs — exclut explicitement le membre vise par son
    /// <c>SpaceMemberId</c>, jamais par son statut courant (qui peut ne pas encore avoir ete
    /// bascule au moment de l'appel).
    /// </summary>
    Task EnsureNotLastManagerAsync(Guid choirId, Guid spaceMemberId, CancellationToken ct = default);
}

public sealed class MembershipService : BaseService, IMembershipService
{
    public MembershipService(IServiceProvider serviceProvider) : base(serviceProvider) { }

    public async Task<bool> IsMemberActiveAsync(Guid choirId, CancellationToken ct = default)
    {
        if (_currentUserRoles.Contains(UserRoleEnum.Admin)) return true;
        if (string.IsNullOrWhiteSpace(_currentUserId)) return false;

        var choir = await _context.Choirs
            .AsNoTracking()
            .Where(c => c.Id == choirId && c.Client.Status == ClientStatusEnum.Active)
            .Select(c => new { c.Status, c.CreatedByUserId })
            .FirstOrDefaultAsync(ct);

        if (choir is null) return false;

        if (choir.Status == ChoirStatusEnum.Archived) return false;

        if (choir.Status == ChoirStatusEnum.Draft)
        {
            if (choir.CreatedByUserId == _currentUserId) return true;
            return await IsManagerAsync(choirId, ct);
        }

        // Publie ou Annule : l'appartenance actif suffit.
        return await _context.SpaceMembers
            .AnyAsync(m => m.ChoirId == choirId
                           && m.UserId == _currentUserId
                           && m.Status == MemberStatusEnum.Active, ct);
    }

    public async Task EnsureMemberActiveAsync(Guid choirId, CancellationToken ct = default)
    {
        if (!await IsMemberActiveAsync(choirId, ct))
            throw new CustomException(HttpStatusCode.Forbidden, "Accès refusé à cette chorale.");
    }

    public async Task<List<Guid>> ChoirsAccessibleAsync(CancellationToken ct = default)
    {
        if (_currentUserRoles.Contains(UserRoleEnum.Admin))
            return await _context.Choirs.Select(c => c.Id).ToListAsync(ct);

        if (string.IsNullOrWhiteSpace(_currentUserId)) return [];

        var viaMembership = await _context.SpaceMembers
            .Where(m => m.UserId == _currentUserId
                        && m.ChoirId != null
                        && m.Status == MemberStatusEnum.Active)
            .Select(m => m.ChoirId!.Value)
            .Distinct()
            .ToListAsync(ct);

        // Un Draft cree par l'utilisateur n'a pas forcement d'appartenance SpaceMember
        // encore posee (les pupitres/membres ne sont pas encore definis) : sans cet ajout,
        // son createur ne verrait jamais sa propre chorale en cours de creation.
        var viaCreation = await _context.Choirs
            .Where(c => c.CreatedByUserId == _currentUserId && c.Status == ChoirStatusEnum.Draft)
            .Select(c => c.Id)
            .ToListAsync(ct);

        var candidates = viaMembership.Concat(viaCreation).Distinct().ToList();
        if (candidates.Count == 0) return [];

        var managerChoirIds = await _context.SpaceMemberRoles
            .Where(r => r.Role == UserRoleEnum.Manager
                        && r.SpaceMember.UserId == _currentUserId
                        && r.SpaceMember.Status == MemberStatusEnum.Active
                        && r.SpaceMember.ChoirId != null
                        && candidates.Contains(r.SpaceMember.ChoirId!.Value))
            .Select(r => r.SpaceMember.ChoirId!.Value)
            .Distinct()
            .ToListAsync(ct);

        return await _context.Choirs
            .Where(c => candidates.Contains(c.Id)
                        && c.Client.Status == ClientStatusEnum.Active
                        && c.Status != ChoirStatusEnum.Archived
                        && (c.Status != ChoirStatusEnum.Draft
                            || c.CreatedByUserId == _currentUserId
                            || managerChoirIds.Contains(c.Id)))
            .Select(c => c.Id)
            .ToListAsync(ct);
    }

    public async Task<bool> CanWriteAsync(Guid choirId, CancellationToken ct = default)
    {
        if (!await IsMemberActiveAsync(choirId, ct)) return false;

        return await IsChoirStatusWritableAsync(choirId, ct);
    }

    public async Task EnsureCanWriteAsync(Guid choirId, CancellationToken ct = default)
    {
        if (!await CanWriteAsync(choirId, ct))
            throw new CustomException(HttpStatusCode.Conflict,
                "Cette chorale n'accepte plus d'écriture dans son état actuel.");
    }

    public async Task EnsureChoirAcceptsWriteAsync(Guid choirId, CancellationToken ct = default)
    {
        if (!await IsChoirStatusWritableAsync(choirId, ct))
            throw new CustomException(HttpStatusCode.Conflict,
                "Cette chorale n'accepte plus d'écriture dans son état actuel.");
    }

    public async Task EnsureNotLastManagerAsync(Guid choirId, Guid spaceMemberId, CancellationToken ct = default)
    {
        var otherActiveManagers = await _context.SpaceMemberRoles
            .CountAsync(r => r.Role == UserRoleEnum.Manager
                              && r.SpaceMemberId != spaceMemberId
                              && r.SpaceMember.ChoirId == choirId
                              && r.SpaceMember.Status == MemberStatusEnum.Active, ct);

        if (otherActiveManagers == 0)
            throw new CustomException(HttpStatusCode.Conflict,
                "Impossible de retirer le dernier chef de chœur de cette chorale. "
                + "Désignez un remplaçant avant de retirer celui-ci.");
    }

    private async Task<bool> IsChoirStatusWritableAsync(Guid choirId, CancellationToken ct)
    {
        var status = await _context.Choirs
            .AsNoTracking()
            .Where(c => c.Id == choirId)
            .Select(c => (ChoirStatusEnum?)c.Status)
            .FirstOrDefaultAsync(ct);

        return status is ChoirStatusEnum.Published or ChoirStatusEnum.Draft;
    }

    private async Task<bool> IsManagerAsync(Guid choirId, CancellationToken ct)
        => await _context.SpaceMemberRoles
            .AnyAsync(r => r.Role == UserRoleEnum.Manager
                           && r.SpaceMember.UserId == _currentUserId
                           && r.SpaceMember.Status == MemberStatusEnum.Active
                           && r.SpaceMember.ChoirId == choirId, ct);
}
