using System.Net;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChoraleBackEnd.Services.ClientServices;

/// <summary>
/// Applique les limites de service d'un client (`10-D23`, `04` § Client).
/// </summary>
/// <remarks>
/// Un plafond qui n'est pas verifie a l'ecriture n'existe pas. Ce service est le seul
/// endroit ou ils sont evalues, pour qu'il n'y ait pas un chemin d'ecriture qui les oublie.
///
/// Deux regles de comportement, issues de `04` § Client :
/// - le refus est explicite et nomme la limite atteinte, jamais une degradation silencieuse ;
/// - un client deja au-dessus d'un plafond abaisse n'est pas ampute : l'existant reste,
///   seules les creations nouvelles sont refusees.
/// </remarks>
public interface IServiceLimitService
{
    Task EnsureCanCreateChoirAsync(Guid clientId, CancellationToken ct = default);
    Task EnsureCanAddMemberAsync(Guid choirId, CancellationToken ct = default);

    /// <summary>
    /// Meme plafond que <see cref="EnsureCanAddMemberAsync"/>, appelable directement par
    /// <c>clientId</c> — cas de <c>ChoirService.CreateAsync</c>, qui amorce le premier chef
    /// de chœur dans la MEME transaction que la chorale et son <c>Space</c> : ceux-ci ne sont
    /// pas encore en base, donc <c>EnsureCanAddMemberAsync(choirId)</c> ne pourrait pas
    /// resoudre le client via <c>Space</c>.
    /// </summary>
    Task EnsureCanAddMemberToNewChoirAsync(Guid clientId, CancellationToken ct = default);

    Task EnsureCanUploadFileAsync(Guid choirId, long sizeBytes, CancellationToken ct = default);
    Task<ClientUsage> GetUsageAsync(Guid clientId, CancellationToken ct = default);
}

/// <summary>Consommation constatee d'un client, en regard de ses plafonds.</summary>
public sealed record ClientUsage(
    int Choirs, int ChoirLimit,
    int Members, int MemberLimit,
    long StorageOctets, long StorageQuotaBytes,
    long MaxFileSizeBytes);

public sealed class ServiceLimitService : BaseService, IServiceLimitService
{
    public ServiceLimitService(IServiceProvider serviceProvider) : base(serviceProvider) { }

    public async Task EnsureCanCreateChoirAsync(Guid clientId, CancellationToken ct = default)
    {
        var client = await LoadClientAsync(clientId, ct);
        EnsureClientActive(client);
        var choirs = await CountChoirsAsync(clientId, ct);

        if (choirs >= client.ChoirLimit)
            throw new CustomException(HttpStatusCode.Conflict,
                $"Limite de chorales atteinte pour ce client : {choirs} sur "
                + $"{client.ChoirLimit}. Contactez l'administration pour la relever.");
    }

    public async Task EnsureCanAddMemberAsync(Guid choirId, CancellationToken ct = default)
    {
        var clientId = await ResolveClientAsync(choirId, ct);
        await EnsureCanAddMemberForClientAsync(clientId, ct);
    }

    public Task EnsureCanAddMemberToNewChoirAsync(Guid clientId, CancellationToken ct = default)
        => EnsureCanAddMemberForClientAsync(clientId, ct);

    /// <summary>
    /// Le plafond porte sur l'ensemble du client, pas sur une chorale : un client ne doit
    /// pas pouvoir le contourner en repartissant ses membres sur plusieurs chorales.
    /// </summary>
    private async Task EnsureCanAddMemberForClientAsync(Guid clientId, CancellationToken ct)
    {
        var client = await LoadClientAsync(clientId, ct);
        EnsureClientActive(client);

        var members = await CountMembersAsync(clientId, ct);

        if (members >= client.MemberLimit)
            throw new CustomException(HttpStatusCode.Conflict,
                $"Limite de membres atteinte pour ce client : {members} sur "
                + $"{client.MemberLimit}. Contactez l'administration pour la relever.");
    }

    public async Task EnsureCanUploadFileAsync(
        Guid choirId, long sizeBytes, CancellationToken ct = default)
    {
        if (sizeBytes <= 0)
            throw new CustomException(HttpStatusCode.BadRequest, "Fichier vide.");

        var clientId = await ResolveClientAsync(choirId, ct);
        var client = await LoadClientAsync(clientId, ct);
        EnsureClientActive(client);

        if (sizeBytes > client.MaxFileSizeBytes)
            throw new CustomException(HttpStatusCode.RequestEntityTooLarge,
                $"Fichier trop volumineux : {EnMo(sizeBytes)} Mo pour un maximum de "
                + $"{EnMo(client.MaxFileSizeBytes)} Mo.");

        var utilise = await ComputeStorageAsync(clientId, ct);

        if (utilise + sizeBytes > client.StorageQuotaBytes)
            throw new CustomException(HttpStatusCode.Conflict,
                // Ne pas conseiller d'archive : `Archivee` ne met pas `IsDeleted`, et le
                // fichier reste sur le disque. Suivre ce conseil ne liberait rien.
                $"Quota de stockage atteint pour ce client : {EnMo(utilise)} Mo utilises sur "
                + $"{EnMo(client.StorageQuotaBytes)} Mo. Contactez l'administration pour "
                + "relever le quota.");
    }

    public async Task<ClientUsage> GetUsageAsync(
        Guid clientId, CancellationToken ct = default)
    {
        var client = await LoadClientAsync(clientId, ct);

        var choirs = await CountChoirsAsync(clientId, ct);
        var members = await CountMembersAsync(clientId, ct);
        var storage = await ComputeStorageAsync(clientId, ct);

        return new ClientUsage(
            choirs, client.ChoirLimit,
            members, client.MemberLimit,
            storage, client.StorageQuotaBytes,
            client.MaxFileSizeBytes);
    }

    /// <summary>
    /// Count de chorales du client comptant pour son plafond.
    /// </summary>
    /// <remarks>
    /// Une chorale <c>Archive</c> (migration 13) n'occupe pas de place — cohérent avec le
    /// comportement livré au lot 3, où l'archivage libérait déjà le plafond (à l'époque via
    /// <c>IsDeleted</c>). Depuis ce lot, l'archivage ne touche plus <c>IsDeleted</c> : c'est
    /// désormais cette exclusion explicite sur <c>Status</c> qui porte la garantie, le filtre
    /// de requête par défaut (qui ne retire que le supprimé) ne suffisant plus seul.
    /// </remarks>
    private Task<int> CountChoirsAsync(Guid clientId, CancellationToken ct)
        => _context.Choirs.CountAsync(
            c => c.ClientId == clientId && c.Status != ChoirStatusEnum.Archived, ct);

    /// <summary>
    /// Count de personnes distinctes membres d'une chorale du client.
    /// </summary>
    /// <remarks>
    /// Trois pieges, tous presents dans la premiere version.
    ///
    /// Il existe une ligne <c>SpaceMember</c> <b>par espace</b>, et les participations aux
    /// events en portent une aussi, avec le meme <c>ChoirId</c>. Count les lignes
    /// revenait donc a count un membre autant de fois qu'il participe d'events : une
    /// chorale de 30 personnes et 10 events pouvait afficher 300 « membres » et bloquer
    /// un client legitime. On restreint a l'espace-chorale, ou <c>SpaceId == ChoirId</c>.
    ///
    /// Ensuite, la meme personne peut appartenir a plusieurs chorales du meme client : d'ou
    /// le <c>Distinct</c> sur l'utilisateur.
    ///
    /// Enfin, un membre archive n'occupe pas de place.
    /// </remarks>
    private Task<int> CountMembersAsync(Guid clientId, CancellationToken ct)
        => _context.SpaceMembers
            .Where(m => m.ChoirId != null
                        && m.SpaceId == m.ChoirId
                        && m.Status != MemberStatusEnum.Archived
                        && _context.Choirs.Any(c => c.Id == m.ChoirId && c.ClientId == clientId))
            .Select(m => m.UserId)
            .Distinct()
            .CountAsync(ct);

    /// <summary>
    /// Somme des deux types de contenu. Le quota est global au client : separer partitions
    /// et enregistrements donnerait deux plafonds a maintenir pour une seule ressource
    /// physique, le disque.
    /// </summary>
    /// <remarks>
    /// <c>IgnoreQueryFilters</c> est deliberement present : rien n'est supprime
    /// physiquement en V1, donc un contenu soft-delete occupe toujours le disque. Le
    /// laisser hors du total rendait le quota contournable en boucle — upload, delete
    /// logiquement, redeposer — pendant que le stockage reel se remplissait.
    /// </remarks>
    private async Task<long> ComputeStorageAsync(Guid clientId, CancellationToken ct)
    {
        // IgnoreQueryFilters porte sur l'arbre de requete entier, sous-requetes comprises.
        var scores = await _context.Scores
            .IgnoreQueryFilters()
            .Where(p => _context.Choirs.Any(c => c.Id == p.Song.ChoirId && c.ClientId == clientId))
            .SumAsync(p => (long?)p.SizeBytes, ct) ?? 0L;

        var recordings = await _context.Recordings
            .IgnoreQueryFilters()
            .Where(e => _context.Choirs.Any(c => c.Id == e.ChoirOwnerId && c.ClientId == clientId))
            .SumAsync(e => (long?)e.SizeBytes, ct) ?? 0L;

        return scores + recordings;
    }

    /// <summary>
    /// Resout le client d'un espace, chorale ou evenement confondus (`10-D23`). Chemin
    /// unique : depuis qu'<see cref="Space"/> porte son propre <c>ClientId</c>, un
    /// evenement autonome (sans chorale porteuse) est soumis aux memes plafonds que
    /// n'importe quelle chorale, via son propre client de rattachement.
    /// </summary>
    private async Task<Guid> ResolveClientAsync(Guid spaceId, CancellationToken ct)
        => await _context.Spaces
               .Where(e => e.Id == spaceId)
               .Select(e => (Guid?)e.ClientId)
               .FirstOrDefaultAsync(ct)
           ?? throw new KeyNotFoundException($"Space {spaceId} not found.");

    private async Task<Client> LoadClientAsync(Guid clientId, CancellationToken ct)
        => await _context.Clients
               .AsNoTracking()
               .FirstOrDefaultAsync(c => c.Id == clientId, ct)
           ?? throw new KeyNotFoundException($"Client {clientId} not found.");

    /// <summary>
    /// Un client suspendu ou archive n'accepte plus aucune ecriture — chorale, membre ou
    /// fichier confondus (`10-D23`). L'autorisation HTTP le bloque deja indirectement (un
    /// role scope suspendu ne se resout plus), mais cette regle merite d'etre vraie ici
    /// aussi : un appel direct au service ne doit pas la contourner.
    /// </summary>
    private static void EnsureClientActive(Client client)
    {
        if (client.Status != ClientStatusEnum.Active)
            throw new CustomException(HttpStatusCode.Forbidden,
                "Ce client n'est pas actif : aucune écriture n'est autorisée.");
    }

    private static long EnMo(long octets) => octets / (1024 * 1024);
}
