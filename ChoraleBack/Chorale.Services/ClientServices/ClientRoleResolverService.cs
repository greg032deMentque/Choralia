using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using Microsoft.EntityFrameworkCore;

namespace ChoraleBackEnd.Services.ClientServices;

public interface IClientRoleResolverService
{
    /// <summary>
    /// Roles detenus par un utilisateur sur un client donne. Vide si aucun.
    /// </summary>
    Task<HashSet<UserRoleEnum>> ResolveRolesAsync(
        string userId, Guid clientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Identifiants des clients sur lesquels l'utilisateur detient au moins un role.
    /// </summary>
    Task<List<Guid>> ResolveClientIdsAsync(
        string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Client d'une chorale EXISTANTE, tel que stocke en base. Destine a
    /// <c>ClientRoleAuthorizationHandler</c> pour les actions qui portent sur une chorale
    /// deja creee (Update, Delete) : le client s'y deduit de la ressource visee, jamais
    /// d'une valeur declaree par l'appelant (route ou corps), pour que la policy et le
    /// service verifient toujours la MEME valeur.
    /// </summary>
    Task<Guid?> ResolveChoirClientIdAsync(Guid choirId, CancellationToken cancellationToken = default);
}

/// <remarks>
/// N'herite deliberement PAS de <c>BaseService</c>, contrairement aux ~37 autres services.
/// Ce resolveur recoit le <c>userId</c> en PARAMETRE et sert le handler d'autorisation, donc
/// avant qu'un « utilisateur courant » existe : <c>BaseService</c> lui donnerait un
/// <c>_currentUserId</c> qu'il ne doit jamais consulter, et tirerait <c>IHttpContextAccessor</c>
/// dans le chemin d'autorisation. Ecart assume, pas un oubli — ne pas « corriger » en revue.
/// </remarks>
public sealed class ClientRoleResolverService : IClientRoleResolverService
{
    private readonly ChoraleDbContext _context;

    public ClientRoleResolverService(ChoraleDbContext context)
    {
        _context = context;
    }

    public async Task<HashSet<UserRoleEnum>> ResolveRolesAsync(
        string userId, Guid clientId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return [];

        // Un client suspendu ou archive ne confere plus aucun role, y compris a son propre
        // responsable : sinon la suspension serait contournable par la personne meme dont on
        // veut suspendre l'acces.
        var roles = await _context.ClientMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId
                        && m.ClientId == clientId
                        && m.Client.Status == ClientStatusEnum.Active)
            .Select(m => m.Role)
            .ToListAsync(cancellationToken);

        return [.. roles];
    }

    public async Task<List<Guid>> ResolveClientIdsAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return [];

        return await _context.ClientMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.Client.Status == ClientStatusEnum.Active)
            .Select(m => m.ClientId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<Guid?> ResolveChoirClientIdAsync(Guid choirId, CancellationToken cancellationToken = default)
        => await _context.Choirs
            .AsNoTracking()
            .Where(c => c.Id == choirId)
            .Select(c => (Guid?)c.ClientId)
            .FirstOrDefaultAsync(cancellationToken);
}
