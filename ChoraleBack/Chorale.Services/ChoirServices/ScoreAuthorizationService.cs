using System.Net;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.AuthServices;

namespace ChoraleBackEnd.Services.ChoirServices;

/// <summary>
/// Regles d'acces propres aux partitions. Les primitives de role et les gardes communes au
/// contenu de chorale vivent dans <see cref="IChoirAuthorizationService"/>.
/// </summary>
public interface IScoreAuthorizationService
{
    /// <summary>
    /// Accès en lecture à une partition précise. Exige que la navigation <c>Song</c> soit
    /// chargée : la chorale de rattachement n'est lisible que par elle.
    /// </summary>
    Task EnsureReadAsync(Score score, CancellationToken ct = default);

    Task<IQueryable<Score>> RestrictDraftVisibilityAsync(IQueryable<Score> query, CancellationToken ct = default);
}

public sealed class ScoreAuthorizationService : BaseService, IScoreAuthorizationService
{
    private readonly IChoirAuthorizationService _choirAuthorization;
    private readonly IMembershipService _membershipService;

    public ScoreAuthorizationService(
        IServiceProvider serviceProvider,
        IChoirAuthorizationService choirAuthorization,
        IMembershipService membershipService)
        : base(serviceProvider)
    {
        _choirAuthorization = choirAuthorization;
        _membershipService = membershipService;
    }

    public async Task<IQueryable<Score>> RestrictDraftVisibilityAsync(
        IQueryable<Score> query, CancellationToken ct = default)
    {
        if (_choirAuthorization.IsAdmin()) return query;

        var choirsManager = await _choirAuthorization.ChoirsManagerAsync(ct);
        var choirsAccessible = await _membershipService.ChoirsAccessibleAsync(ct);

        return query
            .Where(p => choirsAccessible.Contains(p.Song.ChoirId))
            .Where(p =>
                p.Status != ScoreStatusEnum.Draft
                || p.OwnerUserId == _currentUserId
                || choirsManager.Contains(p.Song.ChoirId));
    }

    public async Task EnsureReadAsync(Score score, CancellationToken ct = default)
    {
        if (_choirAuthorization.IsAdmin()) return;

        await _membershipService.EnsureMemberActiveAsync(score.Song.ChoirId, ct);

        if (await _choirAuthorization.IsManagerChoirAsync(score.Song.ChoirId, ct)) return;

        if (score.Status == ScoreStatusEnum.Draft)
        {
            if (score.OwnerUserId == _currentUserId) return;
            throw new CustomException(
                HttpStatusCode.Forbidden,
                "Ce brouillon n'est visible que par son créateur ou un chef de chœur.");
        }
    }
}
