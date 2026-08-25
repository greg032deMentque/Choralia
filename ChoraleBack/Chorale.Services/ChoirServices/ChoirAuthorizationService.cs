using System.Net;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.AuthServices;
using Microsoft.EntityFrameworkCore;

namespace ChoraleBackEnd.Services.ChoirServices;

/// <summary>
/// Primitives de role et gardes d'acces communes au contenu d'une chorale. Source unique :
/// ces regles etaient recopiees a l'identique dans <c>ScoreAuthorizationService</c>,
/// <c>RecordingService</c>, <c>SongListService</c>, <c>EventAuthorizationService</c> et
/// <c>SongService</c>.
/// </summary>
/// <remarks>
/// Ce qui n'a deliberement PAS ete remonte ici, parce que les implementations divergent et
/// que les aligner serait un changement de regle d'acces, pas une factorisation :
/// <list type="bullet">
/// <item><c>ChoirService.EnsureManagerChoirNoAdminBypassAsync</c> — meme requete que
/// <see cref="IsManagerChoirAsync"/>, mais SANS bypass Admin (decision `10-D23`) et via
/// <c>SpaceRoleResolverService</c> plutot que <c>SpaceMemberRoles</c> directement. Le nom
/// porte l'ecart pour qu'il ne soit pas confondu avec <see cref="EnsureManagerChoirAsync"/>.</item>
/// <item><c>EventAuthorizationService.IsMemberChoirActiveAsync</c> — interroge
/// <c>SpaceMembers</c> directement (statut du membre seul), la ou
/// <c>MembershipService.IsMemberActiveAsync</c> (utilisee par
/// <see cref="EnsureVoicePartWriteAccessAsync"/> et par les appelants qui verifient
/// l'appartenance) exige en plus un client actif, une chorale non archivee, et pour un
/// brouillon le createur ou un Manager. Fusionner restreindrait la visibilite des evenements
/// aux membres de clients actifs, et l'ouvrirait aux Admin — deux changements de regle
/// metier, pas une factorisation. Seul appelant : <c>EventService.GetPagedAsync</c>.</item>
/// </list>
/// Loger ces primitives sur <c>BaseService</c> aurait evite tout changement de constructeur,
/// mais <c>BaseService</c> est la base de tous les services (Auth, Client, User, Onboarding) :
/// y mettre de l'autorisation chorale en ferait une god-classe.
/// </remarks>
public interface IChoirAuthorizationService
{
    bool IsAdmin();

    Task<bool> IsManagerChoirAsync(Guid choirId, CancellationToken ct = default);

    Task<bool> IsSectionLeaderVoicePartAsync(Guid choirId, VoicePartEnum voicePart, CancellationToken ct = default);

    /// <summary>Chorales ou l'utilisateur courant est <c>Manager</c>. Destinee aux requetes de liste.</summary>
    Task<List<Guid>> ChoirsManagerAsync(CancellationToken ct = default);

    /// <summary>
    /// Reservee au Responsable de la chorale (avec bypass Admin). <paramref name="choirId"/>
    /// est nullable pour couvrir l'evenement autonome (sans chorale de rattachement) :
    /// <c>null</c> leve toujours <c>403</c>.
    /// </summary>
    Task EnsureManagerChoirAsync(Guid? choirId, CancellationToken ct = default);

    Task EnsureVoicePartWriteAccessAsync(Guid choirId, VoicePartEnum? targetVoicePart, CancellationToken ct = default);
}

public sealed class ChoirAuthorizationService : BaseService, IChoirAuthorizationService
{
    private readonly IMembershipService _membershipService;

    public ChoirAuthorizationService(
        IServiceProvider serviceProvider,
        IMembershipService membershipService)
        : base(serviceProvider)
    {
        _membershipService = membershipService;
    }

    public bool IsAdmin() => _currentUserRoles.Contains(UserRoleEnum.Admin);

    public async Task<bool> IsManagerChoirAsync(Guid choirId, CancellationToken ct = default)
        => await _context.SpaceMemberRoles
            .AnyAsync(r => r.Role == UserRoleEnum.Manager
                && r.SpaceMember.ChoirId == choirId
                && r.SpaceMember.UserId == _currentUserId, ct);

    public async Task<bool> IsSectionLeaderVoicePartAsync(
        Guid choirId, VoicePartEnum voicePart, CancellationToken ct = default)
        => await _context.Sections.AnyAsync(
            p => p.ChoirId == choirId && p.VoicePart == voicePart && p.SectionLeaderId == _currentUserId, ct);

    public async Task<List<Guid>> ChoirsManagerAsync(CancellationToken ct = default)
        => await _context.SpaceMemberRoles
            .AsNoTracking()
            .Where(r => r.Role == UserRoleEnum.Manager
                && r.SpaceMember.UserId == _currentUserId
                && r.SpaceMember.ChoirId.HasValue)
            .Select(r => r.SpaceMember.ChoirId!.Value)
            .ToListAsync(ct);

    public async Task EnsureManagerChoirAsync(Guid? choirId, CancellationToken ct = default)
    {
        if (IsAdmin()) return;
        if (choirId is null || !await IsManagerChoirAsync(choirId.Value, ct))
            throw new CustomException(HttpStatusCode.Forbidden, "Action réservée à un chef de chœur de la chorale.");
    }

    public async Task EnsureVoicePartWriteAccessAsync(
        Guid choirId, VoicePartEnum? targetVoicePart, CancellationToken ct = default)
    {
        // Point de passage unique des ecritures de contenu : la chorale doit accepter
        // l'ecriture, y compris pour l'Admin — voir MembershipService.CanWriteAsync.
        //
        // Cote appelants : ScoreService y passe pour Create/Update/Publish/Archive/Restore/
        // Delete ; RecordingService pour Create/Update/SubmitForReview/Archive/Restore/Delete
        // — ses Publish et Reject n'y passent pas (ils exigent un Manager, pas une voix) et
        // posent EnsureCanWriteAsync explicitement.
        //
        // Les chemins de LECTURE ne doivent surtout pas l'appeler : une chorale Annulee reste
        // lisible alors qu'elle n'accepte plus d'ecriture.
        await _membershipService.EnsureCanWriteAsync(choirId, ct);

        if (IsAdmin()) return;
        if (await IsManagerChoirAsync(choirId, ct)) return;

        if (targetVoicePart.HasValue && await IsSectionLeaderVoicePartAsync(choirId, targetVoicePart.Value, ct)) return;

        throw new CustomException(HttpStatusCode.Forbidden, "Accès refusé à cette voix.");
    }
}
