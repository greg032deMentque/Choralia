using System.Net;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.AuthServices;

namespace ChoraleBackEnd.Services.ChoirServices;

/// <summary>
/// Regles d'acces propres aux enregistrements. Les primitives de role et les gardes communes
/// au contenu de chorale vivent dans <see cref="IChoirAuthorizationService"/>.
/// </summary>
public interface IRecordingAuthorizationService
{
    Task EnsureReadAsync(Recording recording, CancellationToken ct = default);

    Task<IQueryable<Recording>> RestrictVisibilityAsync(IQueryable<Recording> query, CancellationToken ct = default);

    Task EnsureReadPlaylistAsync(Guid? choirId, CancellationToken ct = default);
}

public sealed class RecordingAuthorizationService : BaseService, IRecordingAuthorizationService
{
    private readonly IChoirAuthorizationService _choirAuthorization;
    private readonly IMembershipService _membershipService;

    public RecordingAuthorizationService(
        IServiceProvider serviceProvider,
        IChoirAuthorizationService choirAuthorization,
        IMembershipService membershipService)
        : base(serviceProvider)
    {
        _choirAuthorization = choirAuthorization;
        _membershipService = membershipService;
    }

    // Aucun filtrage par pupitre : un membre voit les enregistrements publies de TOUTES les
    // voix de sa chorale (02-roles-droits-et-visibilite.md § 164). L'ouverture ne porte que
    // sur la lecture — l'ecriture reste cantonnee a la voix du chef de pupitre, via
    // IChoirAuthorizationService.EnsureVoicePartWriteAccessAsync.
    public async Task<IQueryable<Recording>> RestrictVisibilityAsync(
        IQueryable<Recording> query, CancellationToken ct = default)
    {
        if (_choirAuthorization.IsAdmin()) return query;

        var choirsManager = await _choirAuthorization.ChoirsManagerAsync(ct);
        var choirsAccessible = await _membershipService.ChoirsAccessibleAsync(ct);

        return query
            .Where(e => choirsAccessible.Contains(e.ChoirOwnerId))
            .Where(e =>
                choirsManager.Contains(e.ChoirOwnerId)
                || e.CreatorUserId == _currentUserId
                || e.Status != RecordingStatusEnum.Draft
                    && e.Status != RecordingStatusEnum.PendingReview);
    }

    public async Task EnsureReadAsync(Recording recording, CancellationToken ct = default)
    {
        if (_choirAuthorization.IsAdmin()) return;

        await _membershipService.EnsureMemberActiveAsync(recording.ChoirOwnerId, ct);

        if (await _choirAuthorization.IsManagerChoirAsync(recording.ChoirOwnerId, ct)) return;

        if (recording.Status is RecordingStatusEnum.Draft or RecordingStatusEnum.PendingReview)
        {
            if (recording.CreatorUserId == _currentUserId) return;
            throw new CustomException(
                HttpStatusCode.Forbidden,
                "Ce contenu non publié n'est visible que par son créateur ou un chef de chœur.");
        }
    }

    public async Task EnsureReadPlaylistAsync(Guid? choirId, CancellationToken ct = default)
    {
        if (!choirId.HasValue)
            throw new CustomException(
                HttpStatusCode.Conflict,
                "Playlist par voix indisponible pour un événement sans chorale associée.");

        // La voix demandee ne conditionne plus l'acces, seulement le contenu de la playlist :
        // un membre actif consulte celle de n'importe quelle voix de sa chorale, comme il en
        // voit deja les enregistrements (voir RestrictVisibilityAsync).
        await _membershipService.EnsureMemberActiveAsync(choirId.Value, ct);
    }
}
