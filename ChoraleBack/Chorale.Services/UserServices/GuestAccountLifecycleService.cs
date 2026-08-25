using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.ViewModels.Guests;
using Microsoft.EntityFrameworkCore;

namespace ChoraleBackEnd.Services.UserServices;

public interface IGuestAccountLifecycleService
{
    Task<int> AnonymizeUnclaimedGuestsForSpaceAsync(Guid spaceId, CancellationToken ct = default);
    Task<PurgeGuestsResultViewModel> PurgeInactiveGuestsAsync(CancellationToken ct = default);

    /// <summary>
    /// Compte et liste, sans rien purger, les comptes qui seraient concernes par
    /// <see cref="PurgeInactiveGuestsAsync"/> a cet instant. L'ecran doit annoncer ce nombre
    /// avant confirmation — mais la purge recompte toujours au moment de l'action : ne jamais
    /// reutiliser ce nombre comme result de purge.
    /// </summary>
    Task<PurgeCandidatesViewModel> GetPurgeCandidatesAsync(CancellationToken ct = default);
}

public sealed class GuestAccountLifecycleService : BaseService, IGuestAccountLifecycleService
{
    private const int PurgeBatchSize = 500;
    private static readonly TimeSpan InactivityThreshold = TimeSpan.FromDays(365);

    private readonly IAuditLogService _auditLogService;

    public GuestAccountLifecycleService(IServiceProvider serviceProvider, IAuditLogService auditLogService)
        : base(serviceProvider)
    {
        _auditLogService = auditLogService;
    }

    public async Task<int> AnonymizeUnclaimedGuestsForSpaceAsync(Guid spaceId, CancellationToken ct = default)
    {
        var candidateUserIds = await _context.SpaceMembers
            .Where(m => m.SpaceId == spaceId && !m.IsDeleted)
            .Select(m => m.UserId)
            .ToListAsync(ct);

        if (candidateUserIds.Count == 0) return 0;

        var guests = await _context.Users
            .Where(u => candidateUserIds.Contains(u.Id) && u.IsGuestAccount && !u.EmailConfirmed)
            .ToListAsync(ct);

        if (guests.Count == 0) return 0;

        var guestIds = guests.Select(g => g.Id).ToList();
        var userIdsWithOtherMembership = (await _context.SpaceMembers
            .Where(m => guestIds.Contains(m.UserId) && !m.IsDeleted && m.SpaceId != spaceId)
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync(ct))
            .ToHashSet();

        var anonymizedCount = 0;
        foreach (var guest in guests)
        {
            if (userIdsWithOtherMembership.Contains(guest.Id)) continue;

            AnonymizeUser(guest);
            _auditLogService.Record("GuestAccountAnonymized", nameof(User), guest.Id, $"Trigger=EventClosed:{spaceId}");
            anonymizedCount++;
        }

        return anonymizedCount;
    }

    public async Task<PurgeGuestsResultViewModel> PurgeInactiveGuestsAsync(CancellationToken ct = default)
    {
        // Recompte systematique au moment de l'action : un compte revendique (rattache a un
        // espace) entre un eventuel apercu (GetPurgeCandidatsAsync) et cette execution ne doit
        // jamais etre purge, et le nombre retourne doit toujours etre celui reellement traite
        // ICI, jamais celui d'un apercu qui a pu devenir obsolete entre-temps.
        var (candidates, hasMore) = await LoadEligibleCandidatesAsync(ct);

        var anonymizedCount = 0;
        foreach (var candidat in candidates)
        {
            AnonymizeUser(candidat);
            _auditLogService.Record("GuestAccountAnonymized", nameof(User), candidat.Id, "Trigger=PurgeInactive");
            anonymizedCount++;
        }

        // Ligne d'audit agregee en plus des lignes unitaires ci-dessus : le nombre reellement
        // purge doit rester lisible sans avoir a recompter les lignes unitaires du journal.
        _auditLogService.Record("PurgeInactiveGuestsExecuted", nameof(User), "batch",
            $"AnonymizedCount={anonymizedCount}; HasMore={hasMore}");

        await _context.SaveChangesAsync(ct);

        return new PurgeGuestsResultViewModel
        {
            AnonymizedCount = anonymizedCount,
            HasMore = hasMore
        };
    }

    public async Task<PurgeCandidatesViewModel> GetPurgeCandidatesAsync(CancellationToken ct = default)
    {
        var (candidates, hasMore) = await LoadEligibleCandidatesAsync(ct);

        return new PurgeCandidatesViewModel
        {
            Count = candidates.Count,
            HasMore = hasMore,
            Candidates = candidates.Select(c => new PurgeCandidateItemViewModel
            {
                UserId = c.Id,
                Email = c.Email,
                Firstname = c.Firstname,
                Lastname = c.Lastname,
                LastActivityAt = c.LastActive ?? c.LastConnection ?? c.CreatedAt
            }).ToList()
        };
    }

    /// <summary>
    /// Source unique des candidats a la purge, partagee entre l'apercu et l'execution : les
    /// deux doivent appliquer exactement la meme regle d'eligibilite, sous peine de voir
    /// l'apercu annoncer un nombre que l'execution ne peut pas reproduire.
    /// </summary>
    private async Task<(List<User> Candidates, bool HasMore)> LoadEligibleCandidatesAsync(CancellationToken ct)
    {
        var threshold = DateTime.UtcNow - InactivityThreshold;

        var candidates = await _context.Users
            .Where(u => u.IsGuestAccount && !u.EmailConfirmed)
            .Where(u => (u.LastActive ?? u.LastConnection ?? u.CreatedAt) < threshold)
            .OrderBy(u => u.CreatedAt)
            .ThenBy(u => u.Id)
            .Take(PurgeBatchSize + 1)
            .ToListAsync(ct);

        var hasMore = candidates.Count > PurgeBatchSize;
        var batch = hasMore ? candidates.Take(PurgeBatchSize).ToList() : candidates;

        var candidatIds = batch.Select(c => c.Id).ToList();
        var userIdsWithActiveMembership = (await _context.SpaceMembers
            .Where(m => candidatIds.Contains(m.UserId) && !m.IsDeleted)
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync(ct))
            .ToHashSet();

        var eligibles = batch.Where(c => !userIdsWithActiveMembership.Contains(c.Id)).ToList();
        return (eligibles, hasMore);
    }

    private static void AnonymizeUser(User user)
    {
        var anonymizedEmail = $"invite-anonymise-{user.Id}@anonymise.choir.invalid";
        user.Email = anonymizedEmail;
        user.NormalizedEmail = anonymizedEmail.ToUpperInvariant();
        user.UserName = anonymizedEmail;
        user.NormalizedUserName = anonymizedEmail.ToUpperInvariant();
        user.Firstname = "Invité";
        user.Lastname = "Anonymisé";
        user.PhoneNumber = null;
        user.IsDeleted = true;
        user.IsActive = false;
    }
}
