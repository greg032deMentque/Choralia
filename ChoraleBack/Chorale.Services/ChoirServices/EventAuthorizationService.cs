using System.Net;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using Microsoft.EntityFrameworkCore;

namespace ChoraleBackEnd.Services.ChoirServices;

public interface IEventAuthorizationService
{
    bool IsAdmin();
    Task<bool> IsManagerChoirAsync(Guid choirId, CancellationToken ct = default);
    Task<bool> IsOrganizerEventAsync(Guid eventId, CancellationToken ct = default);
    Task<bool> IsSpaceMemberAsync(Guid spaceId, CancellationToken ct = default);
    Task<bool> IsMemberChoirActiveAsync(Guid choirId, CancellationToken ct = default);
    Task EnsureManagerChoirAsync(Guid? choirId, CancellationToken ct = default);
    Task EnsureEventManagerAsync(Event evt, CancellationToken ct = default);
    void EnsureOrganizerAssignable(Guid? choirId);
}

public sealed class EventAuthorizationService : BaseService, IEventAuthorizationService
{
    private readonly IChoirAuthorizationService _choirAuthorization;

    public EventAuthorizationService(
        IServiceProvider serviceProvider,
        IChoirAuthorizationService choirAuthorization)
        : base(serviceProvider)
    {
        _choirAuthorization = choirAuthorization;
    }

    // Reexposees telles quelles : le contrat IEventAuthorizationService est consomme par
    // EventService et EventParticipantService, la source de la regle est desormais unique.
    public bool IsAdmin() => _choirAuthorization.IsAdmin();

    public Task<bool> IsManagerChoirAsync(Guid choirId, CancellationToken ct = default)
        => _choirAuthorization.IsManagerChoirAsync(choirId, ct);

    public async Task<bool> IsOrganizerEventAsync(Guid eventId, CancellationToken ct = default)
        => await _context.SpaceMemberRoles
            .AnyAsync(r => r.Role == UserRoleEnum.Organizer
                && r.SpaceMember.SpaceId == eventId
                && r.SpaceMember.UserId == _currentUserId, ct);

    public async Task<bool> IsSpaceMemberAsync(Guid spaceId, CancellationToken ct = default)
        => await _context.SpaceMembers
            .AsNoTracking()
            .AnyAsync(m => m.SpaceId == spaceId
                && m.UserId == _currentUserId
                && m.Status == MemberStatusEnum.Active, ct);

    public async Task<bool> IsMemberChoirActiveAsync(Guid choirId, CancellationToken ct = default)
        => await _context.SpaceMembers
            .AsNoTracking()
            .AnyAsync(m => m.ChoirId == choirId
                && m.UserId == _currentUserId
                && m.Status == MemberStatusEnum.Active, ct);

    public Task EnsureManagerChoirAsync(Guid? choirId, CancellationToken ct = default)
        => _choirAuthorization.EnsureManagerChoirAsync(choirId, ct);

    public async Task EnsureEventManagerAsync(Event evt, CancellationToken ct = default)
    {
        if (IsAdmin()) return;
        if (evt.ChoirId.HasValue && await IsManagerChoirAsync(evt.ChoirId.Value, ct)) return;
        if (await IsOrganizerEventAsync(evt.Id, ct)) return;
        throw new CustomException(
            HttpStatusCode.Forbidden,
            "Action réservée au chef de chœur de la chorale ou à l'organisateur de l'événement.");
    }

    /// <summary>
    /// D39 (`10-decisions.md`) : le role Organizer n'est affecte qu'a un evenement autonome
    /// (<c>ChoirId</c> nul). Un evenement rattache a une chorale est deja gere par les
    /// Manager de cette chorale (<see cref="EnsureEventManagerAsync"/>) — y ajouter
    /// un Organizer creerait deux chemins d'autorite concurrents sur le meme espace, sans
    /// regle pour les departager.
    /// </summary>
    public void EnsureOrganizerAssignable(Guid? choirId)
    {
        if (choirId.HasValue)
            throw new CustomException(
                HttpStatusCode.Conflict,
                "Le rôle d'organisateur ne peut être affecté qu'à un événement autonome : "
                + "un événement rattaché à une chorale est déjà géré par les chefs de chœur de cette chorale.");
    }
}
