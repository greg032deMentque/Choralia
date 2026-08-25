using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChoraleBackEnd.Services.ChoirServices;

public interface IEventParticipationSeedingService
{
    Task SeedForPublishedEventAsync(Guid eventId, Guid choirId, CancellationToken ct = default);
}

// Regle unique (`04` § Membre/Event) : « un membre actif d'une chorale est participant des
// evenements publies a venir de cette chorale ». Ce service est l'unique point d'ecriture de
// cette regle, sur le seul evenement declencheur : sa publication.
//
// Il ne sauvegarde pas — l'appelant (EventService.ChangeStatusAsync) valide, pour que le
// changement de statut et le peuplement soient atomiques.
public sealed class EventParticipationSeedingService : BaseService, IEventParticipationSeedingService
{
    public EventParticipationSeedingService(IServiceProvider serviceProvider)
        : base(serviceProvider) { }

    public async Task SeedForPublishedEventAsync(Guid eventId, Guid choirId, CancellationToken ct = default)
    {
        // Pas de Distinct() : l'index unique filtre IX_SpaceMembers_UserId_SpaceId
        // (UserId, SpaceId) WHERE [IsDeleted] = 0 interdit deux appartenances vivantes du meme
        // utilisateur au meme espace, et le filtre global de SpaceMember ecarte les lignes
        // supprimees. La liste est donc deja sans doublon. Attention si l'index change : EF
        // InMemory ne l'applique pas, aucun test ne verrait la regression.
        var activeUserIds = await _context.SpaceMembers
            .AsNoTracking()
            .Where(m => m.ChoirId == choirId && m.SpaceId == choirId && m.Status == MemberStatusEnum.Active)
            .Select(m => m.UserId)
            .ToListAsync(ct);

        await EnsureParticipantsAsync(eventId, choirId, activeUserIds, ct);
    }

    /// <summary>
    /// Rattache a l'espace <paramref name="eventId"/> ceux de <paramref name="userIds"/> qui
    /// n'y figurent pas encore. N'annule jamais un rattachement existant, et ne sauvegarde pas.
    /// </summary>
    private async Task EnsureParticipantsAsync(
        Guid eventId, Guid choirId, IReadOnlyCollection<string> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0) return;

        // IgnoreQueryFilters, tous statuts compris : un retrait manuel anterieur
        // (IsDeleted = true) sur l'espace evenement ne doit jamais etre annule par le seeding.
        var alreadyAttached = (await _context.SpaceMembers
                .IgnoreQueryFilters()
                .Where(m => m.SpaceId == eventId)
                .Select(m => m.UserId)
                .ToListAsync(ct))
            .ToHashSet();

        var missing = userIds.Where(userId => !alreadyAttached.Contains(userId)).ToList();

        if (missing.Count == 0) return;

        var newMembers = missing
            .Select(userId => new SpaceMember
            {
                Id = ChoraleDbContext.NewIdGuid(),
                UserId = userId,
                SpaceId = eventId,
                ChoirId = choirId,
                Status = MemberStatusEnum.Active,
                Presence = AttendanceEnum.NoReply,
                IsDeleted = false
            })
            .ToList();

        _context.SpaceMembers.AddRange(newMembers);

        _context.SpaceMemberRoles.AddRange(newMembers.Select(m => new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SpaceMemberId = m.Id,
            Role = UserRoleEnum.Participant
        }));

        // Choix technique ecarte : aucun appel a IServiceLimitService.EnsureCanAddMemberAsync
        // ici. ServiceLimitService.CountMembersAsync (Chorale.Services/ClientServices/
        // ServiceLimitService.cs) ne compte que les lignes ou SpaceId == ChoirId ; une ligne
        // semee ici a SpaceId = EventId != ChoirId et ne peut donc jamais faire depasser le
        // plafond, quel que soit le volume seme.
    }
}
