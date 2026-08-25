using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.Technical;
using Microsoft.EntityFrameworkCore;

namespace ChoraleBackEnd.Services.AuthServices;

/// <summary>
/// Lecture d'un espace (chorale ou evenement) en mode support (`10-D23`).
/// </summary>
/// <remarks>
/// Decision produit : l'administration generale a acces en LECTURE a tout le contenu de
/// toute chorale ou evenement, mais AUCUNE ecriture — et tout acces admin est trace.
///
/// Ce service est le point de passage unique de cette regle, generique aux deux types
/// d'espace grace a <see cref="Space.ClientId"/>. Un non-admin sans role effectif sur
/// l'espace recoit <see cref="KeyNotFoundException"/> (404), pas <see cref="System.Net.HttpStatusCode.Forbidden"/> (403) :
/// reveler qu'une ressource existe a quelqu'un qui n'y a aucun droit est deja une fuite.
/// </remarks>
public interface ISpaceAccessAuditService
{
    Task EnsureReadAsync(Guid spaceId, CancellationToken ct = default);
}

public sealed class SpaceAccessAuditService : BaseService, ISpaceAccessAuditService
{
    private readonly IAuditLogService _auditLogService;
    private readonly ISpaceRoleResolverService _spaceRoleResolverService;

    public SpaceAccessAuditService(
        IServiceProvider serviceProvider,
        IAuditLogService auditLogService,
        ISpaceRoleResolverService spaceRoleResolverService)
        : base(serviceProvider)
    {
        _auditLogService = auditLogService;
        _spaceRoleResolverService = spaceRoleResolverService;
    }

    public async Task EnsureReadAsync(Guid spaceId, CancellationToken ct = default)
    {
        var spaceExists = await _context.Spaces
            .AsNoTracking()
            .AnyAsync(e => e.Id == spaceId, ct);

        if (_currentUserRoles.Contains(UserRoleEnum.Admin))
        {
            if (!spaceExists)
                throw new KeyNotFoundException($"Space {spaceId} not found.");

            // Chaque lecture admin d'un espace laisse une trace, meme si l'espace appartient
            // a un client suspendu : la lecture reste autorisee en mode support, seule
            // l'ecriture est bloquee (policy SpaceManager, sans bypass Admin).
            _auditLogService.Record("AdminSpaceRead", nameof(Space), spaceId.ToString());
            await _context.SaveChangesAsync(ct);
            return;
        }

        if (!spaceExists || string.IsNullOrWhiteSpace(_currentUserId))
            throw new KeyNotFoundException($"Space {spaceId} not found.");

        var rolesBySpace = await _spaceRoleResolverService.ResolveRolesAsync(_currentUserId, [spaceId], ct);
        if (!rolesBySpace.ContainsKey(spaceId))
            throw new KeyNotFoundException($"Space {spaceId} not found.");
    }
}
