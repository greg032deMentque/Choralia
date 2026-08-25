using System.Data;
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
using ChoraleBackEnd.ViewModels.Scores;
using Microsoft.EntityFrameworkCore;

namespace ChoraleBackEnd.Services.ChoirServices;

public interface IScoreService
{
    Task<PagedListViewModel<ScoreViewModel>> GetPagedAsync(ScorePagedFilterViewModel filter, CancellationToken ct = default);
    Task<PagedListViewModel<ScoreViewModel>> GetPagedBySongAsync(ScoreBySongFilterViewModel filter, CancellationToken ct = default);
    Task<ScoreViewModel> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ScoreViewModel> CreateAsync(CreateScoreViewModel model, CancellationToken ct = default);
    Task<ScoreViewModel> UpdateAsync(Guid id, UpdateScoreViewModel model, CancellationToken ct = default);
    Task<ScoreViewModel> PublishAsync(Guid id, CancellationToken ct = default);
    Task<ScoreViewModel> ArchiveAsync(Guid id, CancellationToken ct = default);
    Task<ScoreViewModel> RestoreAsync(Guid id, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<(Stream Content, string ContentType, string FileName, bool DownloadAllowed)> StreamAsync(Guid id, CancellationToken ct = default);
}

public sealed class ScoreService : BaseService, IScoreService
{
    private const string MessageTargetVoicePartRequired =
        "Une voix cible est obligatoire pour une partition par voix.";

    // Liste blanche de tri, partagee par GetPagedAsync et GetPagedBySongAsync (toutes deux
    // passent par PaginateAsync). SongTitle traverse la navigation Chant, deja chargee via
    // Include dans les deux methodes appelantes.
    private static readonly IReadOnlyDictionary<string, Expression<Func<Score, object?>>> ScoresSortableColumns =
        new Dictionary<string, Expression<Func<Score, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Version"] = p => p.Version,
            ["Type"] = p => p.Type,
            ["TargetVoicePart"] = p => p.TargetVoicePart,
            ["Status"] = p => p.Status,
            ["CreatedAt"] = p => p.CreatedAt,
            ["SongTitle"] = p => p.Song.Title
        };

    private const string PublicationConflictMessage =
        "Une partition est déjà publiée pour ce chant, ce type et cette voix. Réessayez.";

    private readonly IScoreFileService _fileService;
    private readonly IScoreAuthorizationService _authorizationService;
    private readonly IChoirAuthorizationService _choirAuthorization;
    private readonly IServiceLimitService _serviceLimitService;
    private readonly IMembershipService _membershipService;

    public ScoreService(
        IServiceProvider serviceProvider,
        IScoreFileService fileService,
        IScoreAuthorizationService authorizationService,
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

    public async Task<PagedListViewModel<ScoreViewModel>> GetPagedAsync(
        ScorePagedFilterViewModel filter, CancellationToken ct = default)
    {
        var query = _context.Scores
            .AsNoTracking()
            .Include(p => p.Song)
            .AsQueryable();

        if (filter.ChoirId.HasValue)
            query = query.Where(p => p.Song.ChoirId == filter.ChoirId.Value);

        if (filter.SongId.HasValue)
            query = query.Where(p => p.SongId == filter.SongId.Value);

        if (filter.Type.HasValue)
            query = query.Where(p => p.Type == filter.Type.Value);

        if (filter.TargetVoicePart.HasValue)
            query = query.Where(p => p.TargetVoicePart == filter.TargetVoicePart.Value);

        if (filter.Status.HasValue)
            query = query.Where(p => p.Status == filter.Status.Value);

        if (!string.IsNullOrWhiteSpace(filter.Filter))
            query = query.Where(p =>
                p.Song.Title.Contains(filter.Filter) || p.Version.Contains(filter.Filter));

        query = await _authorizationService.RestrictDraftVisibilityAsync(query, ct);

        return await PaginateAsync(query, filter, ct);
    }

    public async Task<PagedListViewModel<ScoreViewModel>> GetPagedBySongAsync(
        ScoreBySongFilterViewModel filter, CancellationToken ct = default)
    {
        var song = await _context.Songs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == filter.SongId, ct)
            ?? throw new KeyNotFoundException($"Song {filter.SongId} not found.");

        await _membershipService.EnsureMemberActiveAsync(song.ChoirId, ct);

        var query = _context.Scores
            .AsNoTracking()
            .Include(p => p.Song)
            .Where(p => p.SongId == filter.SongId)
            .AsQueryable();

        query = await _authorizationService.RestrictDraftVisibilityAsync(query, ct);

        return await PaginateAsync(query, filter, ct);
    }

    public async Task<ScoreViewModel> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var score = await LoadWithSongAsync(id, ct);
        await _authorizationService.EnsureReadAsync(score, ct);
        return _mapper.Map<ScoreViewModel>(score);
    }

    public async Task<ScoreViewModel> CreateAsync(CreateScoreViewModel model, CancellationToken ct = default)
    {
        var song = await _context.Songs
            .FirstOrDefaultAsync(c => c.Id == model.SongId, ct)
            ?? throw new KeyNotFoundException($"Song {model.SongId} not found.");

        var targetVoicePart = ResolveTargetVoicePart(model.Type, model.TargetVoicePart);
        await _choirAuthorization.EnsureVoicePartWriteAccessAsync(song.ChoirId, targetVoicePart, ct);
        _fileService.EnsureAllowedFormat(model.File);

        // Meme raison que pour les enregistrements : verifie avant l'ecriture disque, sinon
        // un refus laisserait un fichier orphelin qui compterait dans le quota suivant.
        await _serviceLimitService.EnsureCanUploadFileAsync(
            song.ChoirId, model.File.Length, ct);

        var userId = _currentUserId
            ?? throw new CustomException(HttpStatusCode.Unauthorized, "Utilisateur non authentifié.");

        var fileName = await _fileService.SaveAsync(model.File, ct);

        var score = new Score
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SongId = model.SongId,
            Type = model.Type,
            TargetVoicePart = targetVoicePart,
            Version = model.Version,
            Status = ScoreStatusEnum.Draft,
            OwnerUserId = userId,
            DownloadAllowed = model.DownloadAllowed,
            FilePath = fileName,
            OriginalFileName = model.File.FileName,
            SizeBytes = model.File.Length,
            IsDeleted = false
        };

        _context.Scores.Add(score);
        await _context.SaveChangesAsync(ct);
        return _mapper.Map<ScoreViewModel>(score);
    }

    public async Task<ScoreViewModel> UpdateAsync(Guid id, UpdateScoreViewModel model, CancellationToken ct = default)
    {
        var score = await LoadWithSongAsync(id, ct);
        await _choirAuthorization.EnsureVoicePartWriteAccessAsync(score.Song.ChoirId, WritableVoiceParts(score), ct);

        if (score.Status != ScoreStatusEnum.Draft)
            throw new CustomException(HttpStatusCode.Conflict, "Seule une partition en brouillon peut être modifiée.");

        score.Version = model.Version;
        score.DownloadAllowed = model.DownloadAllowed;
        await _context.SaveChangesAsync(ct);
        return _mapper.Map<ScoreViewModel>(score);
    }

    public async Task<ScoreViewModel> PublishAsync(Guid id, CancellationToken ct = default)
    {
        var score = await LoadWithSongAsync(id, ct);
        await _choirAuthorization.EnsureVoicePartWriteAccessAsync(score.Song.ChoirId, WritableVoiceParts(score), ct);

        if (score.Status != ScoreStatusEnum.Draft)
            throw new CustomException(HttpStatusCode.Conflict, "Seule une partition en brouillon peut être publiée.");

        // La strategie de retry installee par EnableRetryOnFailure (Program.cs,
        // ConfigureDatabase) refuse les transactions ouvertes manuellement : tout bloc
        // transactionnel doit passer par l'execution strategy, qui rejoue l'integralite du
        // delegue quand elle rencontre une erreur transitoire.
        //
        // `score` est charge HORS du delegue et reste suivi par le contexte entre deux
        // tentatives. Les affectations ci-dessous sont idempotentes, donc un rejeu est sur —
        // ne pas introduire ici de logique qui depende de l'etat initial de l'entite.
        var strategy = _context.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async cancellationToken =>
        {
            // Le garde couvre le provider InMemory des tests, qui ne supporte pas les
            // transactions.
            var utiliseTransaction = _context.Database.IsRelational();
            var transaction = utiliseTransaction
                ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                : null;

            try
            {
                var previous = await _context.Scores
                    .Where(p => p.SongId == score.SongId
                        && p.Type == score.Type
                        && p.TargetVoicePart == score.TargetVoicePart
                        && p.Status == ScoreStatusEnum.Published
                        && p.Id != score.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                if (previous is not null)
                    previous.Status = ScoreStatusEnum.Archived;

                score.Status = ScoreStatusEnum.Published;
                score.PublishedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync(cancellationToken);

                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                if (transaction is not null)
                    await transaction.RollbackAsync(cancellationToken);

                throw new CustomException(
                    $"Conflit d'unicité à la publication de la partition {score.Id}.",
                    PublicationConflictMessage,
                    HttpStatusCode.Conflict);
            }
            finally
            {
                if (transaction is not null)
                    await transaction.DisposeAsync();
            }
        }, ct);

        return _mapper.Map<ScoreViewModel>(score);
    }

    public async Task<ScoreViewModel> ArchiveAsync(Guid id, CancellationToken ct = default)
    {
        var score = await LoadWithSongAsync(id, ct);
        await _choirAuthorization.EnsureVoicePartWriteAccessAsync(score.Song.ChoirId, WritableVoiceParts(score), ct);

        if (score.Status == ScoreStatusEnum.Archived)
            throw new CustomException(HttpStatusCode.Conflict, "Cette partition est déjà archivée.");

        score.Status = ScoreStatusEnum.Archived;
        await _context.SaveChangesAsync(ct);
        return _mapper.Map<ScoreViewModel>(score);
    }

    public async Task<ScoreViewModel> RestoreAsync(Guid id, CancellationToken ct = default)
    {
        var score = await LoadWithSongAsync(id, ct);
        await _choirAuthorization.EnsureVoicePartWriteAccessAsync(score.Song.ChoirId, WritableVoiceParts(score), ct);

        if (score.Status != ScoreStatusEnum.Archived)
            throw new CustomException(HttpStatusCode.Conflict, "Seule une partition archivée peut être restaurée.");

        score.Status = ScoreStatusEnum.Draft;
        await _context.SaveChangesAsync(ct);
        return _mapper.Map<ScoreViewModel>(score);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var score = await LoadWithSongAsync(id, ct);
        await _choirAuthorization.EnsureVoicePartWriteAccessAsync(score.Song.ChoirId, WritableVoiceParts(score), ct);

        if (score.Status == ScoreStatusEnum.Published)
            score.Status = ScoreStatusEnum.Archived;

        score.IsDeleted = true;
        await _context.SaveChangesAsync(ct);

        _fileService.Delete(score.FilePath);
    }

    public async Task<(Stream Content, string ContentType, string FileName, bool DownloadAllowed)> StreamAsync(Guid id, CancellationToken ct = default)
    {
        var score = await _context.Scores
            .AsNoTracking()
            .Include(p => p.Song)
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new KeyNotFoundException($"Score {id} not found.");

        await _authorizationService.EnsureReadAsync(score, ct);

        var (content, contentType, fileName) =
            _fileService.OpenForDownload(score.FilePath, score.OriginalFileName);

        return (content, contentType, fileName, score.DownloadAllowed);
    }

    private async Task<PagedListViewModel<ScoreViewModel>> PaginateAsync(
        IQueryable<Score> query, PaginateViewModel pagination, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var items = await query
            .ApplySort(
                pagination.SortActive, pagination.SortDirection, ScoresSortableColumns, p => p.Id,
                q => q.OrderByDescending(p => p.CreatedAt))
            .Skip(pagination.Offset)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        return new PagedListViewModel<ScoreViewModel>
        {
            Items = _mapper.Map<List<ScoreViewModel>>(items),
            TotalCount = total,
            CurrentPage = pagination.Page,
            PageSize = pagination.PageSize
        };
    }

    private async Task<Score> LoadWithSongAsync(Guid id, CancellationToken ct)
        => await _context.Scores
            .Include(p => p.Song)
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new KeyNotFoundException($"Score {id} not found.");

    private static VoicePartEnum? WritableVoiceParts(Score score)
        => score.Type == ScoreTypeEnum.ByVoicePart ? score.TargetVoicePart : null;

    /// <summary>
    /// Refuse un type <c>ByVoicePart</c> sans voix cible, au lieu de le ramener silencieusement
    /// a <c>null</c>.
    /// </summary>
    /// <remarks>
    /// La coercition produisait une partition sans proprietaire de voix : plus aucun chef de
    /// pupitre ne pouvait la modifier
    /// (<see cref="IChoirAuthorizationService.EnsureVoicePartWriteAccessAsync"/> ne recevait
    /// plus de voix a comparer), et l'unicite de publication (<see cref="PublishAsync"/>) la
    /// mettait en concurrence avec les partitions <c>General</c> du meme chant, qui portent
    /// elles aussi <c>TargetVoicePart = null</c>.
    /// </remarks>
    private static VoicePartEnum? ResolveTargetVoicePart(ScoreTypeEnum type, VoicePartEnum? targetVoicePart)
    {
        if (type != ScoreTypeEnum.ByVoicePart) return null;

        return targetVoicePart
            ?? throw new CustomException(HttpStatusCode.BadRequest, MessageTargetVoicePartRequired);
    }
}
