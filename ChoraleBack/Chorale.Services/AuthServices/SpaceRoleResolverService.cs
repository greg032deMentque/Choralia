using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using Microsoft.EntityFrameworkCore;

namespace ChoraleBackEnd.Services.AuthServices;

public interface ISpaceRoleResolverService
{
    Task<Dictionary<Guid, HashSet<UserRoleEnum>>> ResolveRolesAsync(
        string userId, IReadOnlyCollection<Guid>? spaceIds = null, CancellationToken cancellationToken = default);
}

/// <remarks>
/// N'herite deliberement PAS de <c>BaseService</c>, contrairement aux ~37 autres services.
/// Ce resolveur recoit le <c>userId</c> en PARAMETRE et sert le handler d'autorisation, donc
/// avant qu'un « utilisateur courant » existe : <c>BaseService</c> lui donnerait un
/// <c>_currentUserId</c> qu'il ne doit jamais consulter, et tirerait <c>IHttpContextAccessor</c>
/// dans le chemin d'autorisation. Ecart assume, pas un oubli — ne pas « corriger » en revue.
/// </remarks>
public sealed class SpaceRoleResolverService : ISpaceRoleResolverService
{
    private readonly ChoraleDbContext _context;

    public SpaceRoleResolverService(ChoraleDbContext context)
    {
        _context = context;
    }

    public async Task<Dictionary<Guid, HashSet<UserRoleEnum>>> ResolveRolesAsync(
        string userId, IReadOnlyCollection<Guid>? spaceIds = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return [];

        // Statut Active exige : `04` § Membre pose « inactive : accès révoqué » et « archivé :
        // accès révoqué », sans distinction lecture/ecriture. Avant ce filtre, une
        // appartenance Invite, Inactive ou Archive continuait a conferer ses roles ici — la
        // seule source de roles pour TOUTES les policies scopees d'ecriture
        // (SpaceRoleAuthorizationHandler) et pour la lecture en mode support
        // (SpaceAccessAuditService). Un Responsable archive d'une chorale pouvait donc
        // toujours y ecrire, alors que IMembershipService lui refusait deja la lecture :
        // les deux chemins divergeaient. Invite n'a pas besoin d'un traitement a part : la
        // connexion promeut Invite -> Active AVANT toute resolution de roles
        // (AccountService.PromoteInvitedMembershipsAsync, appele et sauvegarde avant
        // la generation du token), donc aucun appelant legitime n'a besoin de resolve des
        // roles pour une appartenance encore Invite.
        var membershipsQuery = _context.SpaceMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.Status == MemberStatusEnum.Active);

        if (spaceIds is { Count: > 0 })
            membershipsQuery = membershipsQuery.Where(m => spaceIds.Contains(m.SpaceId));

        var memberships = await membershipsQuery
            .Select(m => new { m.Id, m.SpaceId, m.Space.SpaceType })
            .ToListAsync(cancellationToken);

        if (memberships.Count == 0)
            return [];

        // Suspension d'un client : elle doit decline l'acces a TOUTES ses chorales d'un
        // seul geste (`10-D23`). Le faire ici plutot que dans chaque service est ce qui rend
        // la garantie vraie — un utilisateur sans role effectif est refuse par toutes les
        // policies scopees, sans qu'aucun appelant ait a y penser.
        //
        // Chemin unique depuis qu'Espace porte son propre ClientId : un espace est bloque si
        // son client n'est pas Active, chorale ou evenement confondus — y compris un evenement
        // autonome, qui a desormais lui aussi un client de rattachement.
        var candidateSpaceIds = memberships.Select(m => m.SpaceId).ToList();

        var allowedSpaceIds = await _context.Spaces
            .AsNoTracking()
            // `!e.IsDeleted` : SpaceConfiguration ne filtre que `!Client.IsDeleted`, jamais
            // l'espace lui-meme (choix documente, delegue au cas par cas). Oublie ici, il
            // laissait l'ex-Responsable d'une chorale supprimee garder son role effectif —
            // donc passer toutes les policies scopees de cet espace.
            .Where(e => candidateSpaceIds.Contains(e.Id)
                        && !e.IsDeleted
                        && e.Client.Status == ClientStatusEnum.Active)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

        memberships = memberships.Where(m => allowedSpaceIds.Contains(m.SpaceId)).ToList();

        if (memberships.Count == 0)
            return [];

        var effectiveSpaceIds = memberships.Select(m => m.SpaceId).ToList();
        var spaceMemberIds = memberships.Select(m => m.Id).ToList();

        var managerSpaceMemberIds = await _context.SpaceMemberRoles
            .AsNoTracking()
            .Where(r => spaceMemberIds.Contains(r.SpaceMemberId) && r.Role == UserRoleEnum.Manager)
            .Select(r => r.SpaceMemberId)
            .ToListAsync(cancellationToken);

        var organizerSpaceMemberIds = await _context.SpaceMemberRoles
            .AsNoTracking()
            .Where(r => spaceMemberIds.Contains(r.SpaceMemberId) && r.Role == UserRoleEnum.Organizer)
            .Select(r => r.SpaceMemberId)
            .ToListAsync(cancellationToken);

        var sectionLeaderSpaceIds = await _context.Sections
            .AsNoTracking()
            .Where(p => effectiveSpaceIds.Contains(p.ChoirId) && p.SectionLeaderId == userId)
            .Select(p => p.ChoirId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var result = new Dictionary<Guid, HashSet<UserRoleEnum>>();
        foreach (var membership in memberships)
        {
            var roleDeBase = membership.SpaceType == SpaceTypeEnum.Choir
                ? UserRoleEnum.Singer
                : UserRoleEnum.Participant;
            var roles = new HashSet<UserRoleEnum> { roleDeBase };

            if (managerSpaceMemberIds.Contains(membership.Id))
                roles.Add(UserRoleEnum.Manager);

            if (organizerSpaceMemberIds.Contains(membership.Id))
                roles.Add(UserRoleEnum.Organizer);

            if (sectionLeaderSpaceIds.Contains(membership.SpaceId))
                roles.Add(UserRoleEnum.SectionLeader);

            result[membership.SpaceId] = roles;
        }

        return result;
    }
}
