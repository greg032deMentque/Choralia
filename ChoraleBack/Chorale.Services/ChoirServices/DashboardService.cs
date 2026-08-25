using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.ViewModels.Dashboard;
using Microsoft.EntityFrameworkCore;

namespace ChoraleBackEnd.Services.ChoirServices;

public interface IDashboardService
{
    Task<ChoirKpiViewModel> GetChoirKpiAsync(Guid choirId, CancellationToken ct = default);
}

/// <summary>
/// Indicateurs du tableau de bord d'une chorale.
/// </summary>
/// <remarks>
/// Chaque indicateur ici a une source reelle (`10-D30`). Ceux qui n'en ont pas ne sont pas
/// renvoyes a zero : ils sont simplement absents du modele, pour qu'aucun ecran ne puisse
/// les afficher par erreur.
/// </remarks>
public sealed class DashboardService : BaseService, IDashboardService
{
    private const int UpcomingEventCount = 5;

    private readonly IMembershipService _membershipService;

    public DashboardService(
        IServiceProvider serviceProvider, IMembershipService membershipService)
        : base(serviceProvider)
    {
        _membershipService = membershipService;
    }

    public async Task<ChoirKpiViewModel> GetChoirKpiAsync(
        Guid choirId, CancellationToken ct = default)
    {
        await _membershipService.EnsureMemberActiveAsync(choirId, ct);

        return new ChoirKpiViewModel
        {
            SongsInRepertoire = await CountSongsActiveAsync(choirId, ct),
            IncompleteSongs = await CountIncompleteSongsAsync(choirId, ct),
            RecordingsPendingReview = await CountRecordingsPendingReviewAsync(choirId, ct),
            Members = await _context.SpaceMembers
                .CountAsync(m => m.ChoirId == choirId
                                 && m.Status == MemberStatusEnum.Active, ct),
            InvitedMembers = await _context.SpaceMembers
                .CountAsync(m => m.ChoirId == choirId
                                 && m.Status == MemberStatusEnum.Invited, ct),
            UpcomingEvents = await LoadUpcomingEventsAsync(choirId, ct)
        };
    }

    private Task<int> CountSongsActiveAsync(Guid choirId, CancellationToken ct)
        => _context.Songs.CountAsync(
            c => c.ChoirId == choirId && c.Status == SongStatusEnum.Active, ct);

    /// <summary>
    /// Reproduit la completude chorale de `10-D10` : une partition publiee, et chaque voix
    /// attendue couverte par un enregistrement publie pour cette voix.
    /// </summary>
    /// <remarks>
    /// Traduit en une seule requete plutot que de reutiliser le calcul par chant de
    /// <c>SongService</c>, qui charge chaque chant avec ses collections — sur un repertoire
    /// entier cela ferait autant d'allers-retours que de chants pour un simple compteur.
    /// La regle est donc dupliquee ici : si elle change, les deux endroits doivent bouger,
    /// et le test de cet indicateur est ce qui le rappellera.
    /// </remarks>
    private Task<int> CountIncompleteSongsAsync(Guid choirId, CancellationToken ct)
        => _context.Songs
            .Where(c => c.ChoirId == choirId && c.Status == SongStatusEnum.Active)
            .Where(c =>
                !c.Scores.Any(p => p.Status == ScoreStatusEnum.Published)
                || c.SongVoicePart.Any(cv =>
                    !c.Recordings.Any(e =>
                        e.Status == RecordingStatusEnum.Published
                        && e.Type == RecordingTypeEnum.ByVoicePart
                        && e.TargetVoicePart == cv.VoicePart)))
            .CountAsync(ct);

    private Task<int> CountRecordingsPendingReviewAsync(Guid choirId, CancellationToken ct)
        => _context.Recordings.CountAsync(
            e => e.ChoirOwnerId == choirId
                 && e.Status == RecordingStatusEnum.PendingReview, ct);

    private async Task<List<NextEventViewModel>> LoadUpcomingEventsAsync(
        Guid choirId, CancellationToken ct)
    {
        var maintenant = DateTime.UtcNow;

        var events = await _context.Events
            .AsNoTracking()
            .Where(e => e.ChoirId == choirId
                        && e.Status == EventStatusEnum.Published
                        && (e.EndDate ?? e.StartDate) >= maintenant)
            .OrderBy(e => e.StartDate)
            .Take(UpcomingEventCount)
            .Select(e => new { e.Id, e.Title, e.Location, e.StartDate })
            .ToListAsync(ct);

        if (events.Count == 0) return [];

        var ids = events.Select(e => e.Id).ToList();

        // Presence : le taux se lit sur les participants de l'espace de l'evenement.
        // `SansReponse` est l'etat initial de tout membre target — il compte donc dans les
        // cibles mais pas dans les reponses (`04` § Presence).
        var presences = await _context.SpaceMembers
            .AsNoTracking()
            .Where(m => ids.Contains(m.SpaceId))
            .GroupBy(m => m.SpaceId)
            .Select(g => new
            {
                SpaceId = g.Key,
                Targets = g.Count(),
                Responses = g.Count(m => m.Presence != null
                                        && m.Presence != AttendanceEnum.NoReply)
            })
            .ToListAsync(ct);

        return [.. events.Select(e =>
        {
            var p = presences.FirstOrDefault(x => x.SpaceId == e.Id);
            var targets = p?.Targets ?? 0;
            var reponses = p?.Responses ?? 0;

            return new NextEventViewModel
            {
                Id = e.Id,
                Title = e.Title,
                Location = e.Location,
                StartDate = e.StartDate,
                Targets = targets,
                Responses = reponses,
                ResponseRate = targets == 0 ? null : (int)Math.Round(100.0 * reponses / targets)
            };
        })];
    }
}
