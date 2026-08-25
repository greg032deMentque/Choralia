using System.Linq.Expressions;
using System.Net;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Helpers;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.Recordings;
using Microsoft.EntityFrameworkCore;

namespace ChoraleBackEnd.Services.ChoirServices;

public interface IRecordingService
{
    Task<PagedListViewModel<RecordingViewModel>> GetPagedAsync(RecordingPagedFilterViewModel filter, CancellationToken ct = default);
    Task<PagedListViewModel<RecordingViewModel>> GetPagedBySongAsync(RecordingBySongFilterViewModel filter, CancellationToken ct = default);
    Task<RecordingViewModel> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<RecordingViewModel> CreateAsync(CreateRecordingViewModel model, CancellationToken ct = default);
    Task<RecordingViewModel> UpdateAsync(Guid id, UpdateRecordingViewModel model, CancellationToken ct = default);
    Task<RecordingViewModel> SubmitForReviewAsync(Guid id, CancellationToken ct = default);
    Task<RecordingViewModel> PublishAsync(Guid id, CancellationToken ct = default);
    Task<RecordingViewModel> RejectAsync(Guid id, CancellationToken ct = default);
    Task<RecordingViewModel> ArchiveAsync(Guid id, CancellationToken ct = default);
    Task<RecordingViewModel> RestoreAsync(Guid id, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<(Stream Content, string ContentType, string FileName, bool DownloadAllowed)> StreamAsync(Guid id, CancellationToken ct = default);
    Task<List<PlaylistTrackViewModel>> GetEventPlaylistByVoicePartAsync(Guid eventId, VoicePartEnum voicePart, CancellationToken ct = default);
}

public sealed class RecordingService : BaseService, IRecordingService
{
    private const string MessageTargetVoicePartRequired =
        "Une voix cible est obligatoire pour un enregistrement par voix.";

    // Liste blanche de tri, partagee par GetPagedAsync et GetPagedBySongAsync (toutes deux
    // passent par PaginateAsync). SongTitle traverse la navigation Chant, deja chargee via
    // Include dans les deux methodes appelantes.
    private static readonly IReadOnlyDictionary<string, Expression<Func<Recording, object?>>> RecordingsSortableColumns =
        new Dictionary<string, Expression<Func<Recording, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Type"] = e => e.Type,
            ["TargetVoicePart"] = e => e.TargetVoicePart,
            ["Status"] = e => e.Status,
            ["Source"] = e => e.Source,
            ["DurationSeconds"] = e => e.DurationSeconds,
            ["CreatedAt"] = e => e.CreatedAt,
            ["SongTitle"] = e => e.Song.Title
        };

    private readonly IRecordingFileService _fileService;
    private readonly IRecordingAuthorizationService _authorizationService;
    private readonly IChoirAuthorizationService _choirAuthorization;
    private readonly IServiceLimitService _serviceLimitService;
    private readonly IMembershipService _membershipService;

    public RecordingService(
        IServiceProvider serviceProvider,
        IRecordingFileService fileService,
        IRecordingAuthorizationService authorizationService,
        IChoirAuthorizationService choirAuthorization,
        IServiceLimitService serviceLimitService,
        IMembershipService membershipService)
        : base(serviceProvider)
    {
        _fileService = fileService;
        _authorizationService = authorizationService;
        _choirAuthorization = choirAuthorization;
        _serviceLimitService = serviceLimitService;
        _membershipService = membershipService;
    }

    public async Task<PagedListViewModel<RecordingViewModel>> GetPagedAsync(
        RecordingPagedFilterViewModel filter, CancellationToken ct = default)
    {
        var query = _context.Recordings
            .AsNoTracking()
            .Include(e => e.Song)
            .AsQueryable();

        if (filter.ChoirId.HasValue)
            query = query.Where(e => e.ChoirOwnerId == filter.ChoirId.Value);

        if (filter.SongId.HasValue)
            query = query.Where(e => e.SongId == filter.SongId.Value);

        if (filter.Type.HasValue)
            query = query.Where(e => e.Type == filter.Type.Value);

        if (filter.TargetVoicePart.HasValue)
            query = query.Where(e => e.TargetVoicePart == filter.TargetVoicePart.Value);

        if (filter.Status.HasValue)
            query = query.Where(e => e.Status == filter.Status.Value);

        if (filter.Source.HasValue)
            query = query.Where(e => e.Source == filter.Source.Value);

        if (!string.IsNullOrWhiteSpace(filter.Filter))
            query = query.Where(e =>
                e.Song.Title.Contains(filter.Filter) || e.ContentOwner.Contains(filter.Filter));

        query = await _authorizationService.RestrictVisibilityAsync(query, ct);

        return await PaginateAsync(query, filter, ct);
    }

    public async Task<PagedListViewModel<RecordingViewModel>> GetPagedBySongAsync(
        RecordingBySongFilterViewModel filter, CancellationToken ct = default)
    {
        var song = await _context.Songs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == filter.SongId, ct)
            ?? throw new KeyNotFoundException($"Song {filter.SongId} not found.");

        await _membershipService.EnsureMemberActiveAsync(song.ChoirId, ct);

        var query = _context.Recordings
            .AsNoTracking()
            .Include(e => e.Song)
            .Where(e => e.SongId == filter.SongId)
            .AsQueryable();

        query = await _authorizationService.RestrictVisibilityAsync(query, ct);

        return await PaginateAsync(query, filter, ct);
    }

    public async Task<RecordingViewModel> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var recording = await LoadAsync(id, ct);
        await _authorizationService.EnsureReadAsync(recording, ct);
        return _mapper.Map<RecordingViewModel>(recording);
    }

    public async Task<RecordingViewModel> CreateAsync(CreateRecordingViewModel model, CancellationToken ct = default)
    {
        var song = await _context.Songs
            .FirstOrDefaultAsync(c => c.Id == model.SongId, ct)
            ?? throw new KeyNotFoundException($"Song {model.SongId} not found.");

        var targetVoicePart = ResolveTargetVoicePart(model.Type, model.TargetVoicePart);
        await _choirAuthorization.EnsureVoicePartWriteAccessAsync(song.ChoirId, targetVoicePart, ct);
        _fileService.EnsureAllowedFormat(model.File);

        // Plafonds du client, verifies AVANT l'ecriture disque : decline apres avoir ecrit
        // laisserait un fichier orphelin qui compterait dans le quota suivant.
        await _serviceLimitService.EnsureCanUploadFileAsync(
            song.ChoirId, model.File.Length, ct);

        var userId = _currentUserId
            ?? throw new CustomException(HttpStatusCode.Unauthorized, "Utilisateur non authentifié.");

        var fileName = await _fileService.SaveAsync(model.File, ct);

        var recording = new Recording
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SongId = model.SongId,
            Type = model.Type,
            TargetVoicePart = targetVoicePart,
            ChoirOwnerId = song.ChoirId,
            CreatorUserId = userId,
            Status = RecordingStatusEnum.Draft,
            Source = model.Source,
            DurationSeconds = model.DurationSeconds,
            PublicationDate = null,
            ContentOwner = model.ContentOwner,
            DownloadAllowed = model.DownloadAllowed,
            FilePath = fileName,
            OriginalFileName = model.File.FileName,
            SizeBytes = model.File.Length,
            IsDeleted = false
        };

        _context.Recordings.Add(recording);
        await _context.SaveChangesAsync(ct);
        return _mapper.Map<RecordingViewModel>(recording);
    }

    public async Task<RecordingViewModel> UpdateAsync(Guid id, UpdateRecordingViewModel model, CancellationToken ct = default)
    {
        var recording = await LoadAsync(id, ct);
        await _choirAuthorization.EnsureVoicePartWriteAccessAsync(recording.ChoirOwnerId, WritableVoiceParts(recording), ct);

        if (recording.Status != RecordingStatusEnum.Draft)
            throw new CustomException(HttpStatusCode.Conflict, "Seul un enregistrement en brouillon peut être modifié.");

        recording.ContentOwner = model.ContentOwner;
        recording.DownloadAllowed = model.DownloadAllowed;
        recording.DurationSeconds = model.DurationSeconds;
        await _context.SaveChangesAsync(ct);
        return _mapper.Map<RecordingViewModel>(recording);
    }

    public async Task<RecordingViewModel> SubmitForReviewAsync(Guid id, CancellationToken ct = default)
    {
        var recording = await LoadAsync(id, ct);
        await _choirAuthorization.EnsureVoicePartWriteAccessAsync(recording.ChoirOwnerId, WritableVoiceParts(recording), ct);

        if (recording.Status != RecordingStatusEnum.Draft)
            throw new CustomException(HttpStatusCode.Conflict, "Seul un brouillon peut être envoyé à validation.");

        recording.Status = RecordingStatusEnum.PendingReview;
        await _context.SaveChangesAsync(ct);
        return _mapper.Map<RecordingViewModel>(recording);
    }

    public async Task<RecordingViewModel> PublishAsync(Guid id, CancellationToken ct = default)
    {
        var recording = await LoadAsync(id, ct);
        await _choirAuthorization.EnsureManagerChoirAsync(recording.ChoirOwnerId, ct);
        await _membershipService.EnsureCanWriteAsync(recording.ChoirOwnerId, ct);

        if (recording.Status != RecordingStatusEnum.PendingReview)
            throw new CustomException(HttpStatusCode.Conflict, "Seul un enregistrement à valider peut être publié.");

        recording.Status = RecordingStatusEnum.Published;
        recording.PublicationDate = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return _mapper.Map<RecordingViewModel>(recording);
    }

    public async Task<RecordingViewModel> RejectAsync(Guid id, CancellationToken ct = default)
    {
        var recording = await LoadAsync(id, ct);
        await _choirAuthorization.EnsureManagerChoirAsync(recording.ChoirOwnerId, ct);
        await _membershipService.EnsureCanWriteAsync(recording.ChoirOwnerId, ct);

        if (recording.Status != RecordingStatusEnum.PendingReview)
            throw new CustomException(HttpStatusCode.Conflict, "Seul un enregistrement à valider peut être rejeté.");

        recording.Status = RecordingStatusEnum.Draft;
        await _context.SaveChangesAsync(ct);
        return _mapper.Map<RecordingViewModel>(recording);
    }

    public async Task<RecordingViewModel> ArchiveAsync(Guid id, CancellationToken ct = default)
    {
        var recording = await LoadAsync(id, ct);
        await _choirAuthorization.EnsureVoicePartWriteAccessAsync(recording.ChoirOwnerId, WritableVoiceParts(recording), ct);

        if (recording.Status == RecordingStatusEnum.Archived)
            throw new CustomException(HttpStatusCode.Conflict, "Cet enregistrement est déjà archivé.");

        recording.Status = RecordingStatusEnum.Archived;
        await _context.SaveChangesAsync(ct);
        return _mapper.Map<RecordingViewModel>(recording);
    }

    public async Task<RecordingViewModel> RestoreAsync(Guid id, CancellationToken ct = default)
    {
        var recording = await LoadAsync(id, ct);
        await _choirAuthorization.EnsureVoicePartWriteAccessAsync(recording.ChoirOwnerId, WritableVoiceParts(recording), ct);

        if (recording.Status != RecordingStatusEnum.Archived)
            throw new CustomException(HttpStatusCode.Conflict, "Seul un enregistrement archivé peut être restauré.");

        recording.Status = RecordingStatusEnum.Draft;
        await _context.SaveChangesAsync(ct);
        return _mapper.Map<RecordingViewModel>(recording);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var recording = await LoadAsync(id, ct);
        await _choirAuthorization.EnsureVoicePartWriteAccessAsync(recording.ChoirOwnerId, WritableVoiceParts(recording), ct);

        recording.IsDeleted = true;
        await _context.SaveChangesAsync(ct);

        _fileService.Delete(recording.FilePath);
    }

    public async Task<(Stream Content, string ContentType, string FileName, bool DownloadAllowed)> StreamAsync(Guid id, CancellationToken ct = default)
    {
        var recording = await _context.Recordings
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new KeyNotFoundException($"Recording {id} not found.");

        await _authorizationService.EnsureReadAsync(recording, ct);

        var (content, contentType, fileName) =
            _fileService.OpenForDownload(recording.FilePath, recording.OriginalFileName);

        return (content, contentType, fileName, recording.DownloadAllowed);
    }

    public async Task<List<PlaylistTrackViewModel>> GetEventPlaylistByVoicePartAsync(
        Guid eventId, VoicePartEnum voicePart, CancellationToken ct = default)
    {
        var evt = await _context.Events
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == eventId, ct)
            ?? throw new KeyNotFoundException($"Event {eventId} not found.");

        await _authorizationService.EnsureReadPlaylistAsync(evt.ChoirId, ct);

        var orderedSongIds = await BuildOrderedSongIdsAsync(eventId, ct);
        if (orderedSongIds.Count == 0)
            return [];

        var recordingsBySong = await LoadRecordingsPublishedBySongAsync(orderedSongIds, voicePart, ct);

        return BuildPlaylist(orderedSongIds, recordingsBySong);
    }

    private async Task<List<Guid>> BuildOrderedSongIdsAsync(Guid eventId, CancellationToken ct)
    {
        var songLists = await _context.SongLists
            .AsNoTracking()
            .Where(d => d.EventId == eventId && d.Status == SongListStatusEnum.Published)
            .OrderBy(d => d.CreatedAt)
            .Include(d => d.SongListSongs.OrderBy(dc => dc.Position))
            .ToListAsync(ct);

        var orderedSongIds = new List<Guid>();
        var seenSongIds = new HashSet<Guid>();
        foreach (var songList in songLists)
        {
            foreach (var songListSong in songList.SongListSongs)
            {
                if (seenSongIds.Add(songListSong.SongId))
                    orderedSongIds.Add(songListSong.SongId);
            }
        }

        return orderedSongIds;
    }

    private async Task<Dictionary<Guid, List<Recording>>> LoadRecordingsPublishedBySongAsync(
        List<Guid> songIds, VoicePartEnum voicePart, CancellationToken ct)
    {
        var recordings = await _context.Recordings
            .AsNoTracking()
            .Include(e => e.Song)
            .Where(e => songIds.Contains(e.SongId)
                && e.Type == RecordingTypeEnum.ByVoicePart
                && e.TargetVoicePart == voicePart
                && e.Status == RecordingStatusEnum.Published)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct);

        return recordings
            .GroupBy(e => e.SongId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    private static List<PlaylistTrackViewModel> BuildPlaylist(
        List<Guid> orderedSongIds, Dictionary<Guid, List<Recording>> recordingsBySong)
    {
        var playlist = new List<PlaylistTrackViewModel>();
        foreach (var songId in orderedSongIds)
        {
            if (!recordingsBySong.TryGetValue(songId, out var songRecordings))
                continue;

            foreach (var recording in songRecordings)
            {
                playlist.Add(new PlaylistTrackViewModel
                {
                    RecordingId = recording.Id,
                    SongId = recording.SongId,
                    SongTitle = recording.Song.Title,
                    TargetVoicePart = recording.TargetVoicePart,
                    DurationSeconds = recording.DurationSeconds,
                    Position = playlist.Count
                });
            }
        }

        return playlist;
    }

    private async Task<PagedListViewModel<RecordingViewModel>> PaginateAsync(
        IQueryable<Recording> query, PaginateViewModel pagination, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var items = await query
            .ApplySort(
                pagination.SortActive, pagination.SortDirection, RecordingsSortableColumns, e => e.Id,
                q => q.OrderByDescending(e => e.CreatedAt))
            .Skip(pagination.Offset)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        return new PagedListViewModel<RecordingViewModel>
        {
            Items = _mapper.Map<List<RecordingViewModel>>(items),
            TotalCount = total,
            CurrentPage = pagination.Page,
            PageSize = pagination.PageSize
        };
    }

    private async Task<Recording> LoadAsync(Guid id, CancellationToken ct)
        => await _context.Recordings
            .FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new KeyNotFoundException($"Recording {id} not found.");

    private static VoicePartEnum? WritableVoiceParts(Recording recording)
        => recording.Type == RecordingTypeEnum.ByVoicePart ? recording.TargetVoicePart : null;

    /// <summary>
    /// Refuse un type <c>ByVoicePart</c> sans voix cible, au lieu de le ramener silencieusement
    /// a <c>null</c>.
    /// </summary>
    /// <remarks>
    /// La coercition produisait un enregistrement que
    /// <see cref="IRecordingAuthorizationService.RestrictVisibilityAsync"/> et
    /// <see cref="IRecordingAuthorizationService.EnsureReadAsync"/> ne traitaient pas pareil —
    /// deux regles qui divergent sur la meme donnee. Le contenu n'a alors plus de proprietaire
    /// de voix : ni son chef de pupitre ni personne ne peut plus le modifier
    /// (<see cref="IChoirAuthorizationService.EnsureVoicePartWriteAccessAsync"/> ne recoit
    /// plus de voix a comparer).
    /// </remarks>
    private static VoicePartEnum? ResolveTargetVoicePart(RecordingTypeEnum type, VoicePartEnum? targetVoicePart)
    {
        if (type != RecordingTypeEnum.ByVoicePart) return null;

        return targetVoicePart
            ?? throw new CustomException(HttpStatusCode.BadRequest, MessageTargetVoicePartRequired);
    }
}
