using System.Linq.Expressions;
using System.Net;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Helpers;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.ViewModels;
using Microsoft.EntityFrameworkCore;
using ChoraleBackEnd.ViewModels.Songs;

namespace ChoraleBackEnd.Services.ChoirServices;

public interface ISongService
{
    Task<PagedListViewModel<SongViewModel>> GetPagedAsync(SongPagedFilterViewModel filter, CancellationToken ct = default);
    Task<PagedListViewModel<SongViewModel>> GetPagedByChoirAsync(SongByChoirFilterViewModel filter, CancellationToken ct = default);
    Task<SongViewModel> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<SongViewModel> CreateAsync(SongViewModel model, CancellationToken ct = default);
    Task<SongViewModel> UpdateAsync(SongViewModel model, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class SongService : BaseService, ISongService
{
    // Liste blanche de tri : sans elle, Skip/Take sur une requete non triee n'est pas
    // reproductible (deux pages consecutives peuvent se recouvrir ou perdre des lignes) — voir
    // TriHelper. Tri par defaut par Titre, departage sur Id.
    private static readonly IReadOnlyDictionary<string, Expression<Func<Song, object?>>> SongsSortableColumns =
        new Dictionary<string, Expression<Func<Song, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Title"] = c => c.Title,
            ["Composer"] = c => c.Composer,
            ["Status"] = c => c.Status,
            ["CreatedAt"] = c => c.CreatedAt
        };

    private readonly IMembershipService _membershipService;
    private readonly IChoirAuthorizationService _choirAuthorization;

    public SongService(
        IServiceProvider serviceProvider,
        IMembershipService membershipService,
        IChoirAuthorizationService choirAuthorization)
        : base(serviceProvider)
    {
        _membershipService = membershipService;
        _choirAuthorization = choirAuthorization;
    }

    public async Task<PagedListViewModel<SongViewModel>> GetPagedAsync(
        SongPagedFilterViewModel filter, CancellationToken ct = default)
    {
        var query = _context.Songs
            .AsNoTracking()
            .Include(c => c.SongVoicePart)
            .AsQueryable();

        // Restriction d'appartenance appliquee INCONDITIONNELLEMENT et en premier. Le filtre
        // ChoirId ci-dessous est fourni par l'appelant et facultatif : s'appuyer sur lui
        // laissait le repertoire de toutes les chorales de tous les clients accessible a
        // tout compte authentifie — reproduit entre deux clients distincts.
        query = await RestrictToAccessibleChoirsAsync(query, ct);

        if (filter.ChoirId.HasValue)
            query = query.Where(c => c.ChoirId == filter.ChoirId.Value);

        if (filter.VoicePart.HasValue)
            query = query.Where(c => c.SongVoicePart.Any(cv => cv.VoicePart == filter.VoicePart.Value));

        if (filter.Status.HasValue)
            query = query.Where(c => c.Status == filter.Status.Value);

        if (filter.Priority.HasValue)
            query = query.Where(c => c.Priority == filter.Priority.Value);

        if (!string.IsNullOrWhiteSpace(filter.Filter))
            query = query.Where(c =>
                c.Title.Contains(filter.Filter) ||
                c.Composer != null && c.Composer.Contains(filter.Filter));

        var total = await query.CountAsync(ct);
        var items = await query
            .ApplySort(
                filter.SortActive, filter.SortDirection, SongsSortableColumns, c => c.Id,
                q => q.OrderBy(c => c.Title).ThenBy(c => c.Id))
            .Skip(filter.Offset)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return new PagedListViewModel<SongViewModel>
        {
            Items = _mapper.Map<List<SongViewModel>>(items),
            TotalCount = total,
            CurrentPage = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<PagedListViewModel<SongViewModel>> GetPagedByChoirAsync(
        SongByChoirFilterViewModel filter, CancellationToken ct = default)
    {
        // La chorale est designee par l'appelant : son appartenance doit etre verifiee, sans
        // quoi il suffit de connaitre un identifiant pour lire le repertoire target.
        await _membershipService.EnsureMemberActiveAsync(filter.ChoirId, ct);

        var query = _context.Songs
            .AsNoTracking()
            .Include(c => c.SongVoicePart)
            .Where(c => c.ChoirId == filter.ChoirId)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Filter))
            query = query.Where(c =>
                c.Title.Contains(filter.Filter) ||
                c.Composer != null && c.Composer.Contains(filter.Filter));

        var total = await query.CountAsync(ct);
        var items = await query
            .ApplySort(
                filter.SortActive, filter.SortDirection, SongsSortableColumns, c => c.Id,
                q => q.OrderBy(c => c.Title).ThenBy(c => c.Id))
            .Skip(filter.Offset)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return new PagedListViewModel<SongViewModel>
        {
            Items = _mapper.Map<List<SongViewModel>>(items),
            TotalCount = total,
            CurrentPage = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<SongViewModel> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var song = await _context.Songs
            .AsNoTracking()
            .Include(c => c.SongVoicePart)
            .Include(c => c.Scores)
            .Include(c => c.Recordings)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException($"Song {id} not found.");

        await _membershipService.EnsureMemberActiveAsync(song.ChoirId, ct);

        var viewModel = _mapper.Map<SongViewModel>(song);
        ApplyCompleteness(song, viewModel);
        return viewModel;
    }

    public async Task<SongViewModel> CreateAsync(SongViewModel model, CancellationToken ct = default)
    {
        // `02` § Matrice : create et update un chant releve du Responsable seul. Le simple
        // controle d'appartenance laissait n'importe quel chanteur ecrire dans le repertoire,
        // et le role est verifie sur la chorale de la RESSOURCE — pas seulement sur celle du
        // scope — ce qui ferme aussi l'ecriture trans-chorale.
        await _membershipService.EnsureMemberActiveAsync(model.ChoirId, ct);
        await _membershipService.EnsureCanWriteAsync(model.ChoirId, ct);
        await EnsureManagerAsync(model.ChoirId, ct);

        var song = _mapper.Map<Song>(model);
        song.Id = ChoraleDbContext.NewIdGuid();
        // Le profil ignore ChoirId (voir SongViewModelMappingProfile) : c'est ici, et
        // seulement ici, que le rattachement se pose — apres les trois gardes ci-dessus.
        song.ChoirId = model.ChoirId;
        song.SongVoicePart = model.VoiceParts
            .Distinct()
            .Select(v => new SongVoicePart { Id = ChoraleDbContext.NewIdGuid(), SongId = song.Id, VoicePart = v })
            .ToList();

        _context.Songs.Add(song);
        await _context.SaveChangesAsync(ct);
        return _mapper.Map<SongViewModel>(song);
    }

    public async Task<SongViewModel> UpdateAsync(SongViewModel model, CancellationToken ct = default)
    {
        if (model.Id is null)
            throw new CustomException(HttpStatusCode.BadRequest, "Id requis.");

        var song = await _context.Songs
            .Include(c => c.SongVoicePart)
            .FirstOrDefaultAsync(c => c.Id == model.Id, ct)
            ?? throw new KeyNotFoundException($"Song {model.Id} not found.");

        await _membershipService.EnsureMemberActiveAsync(song.ChoirId, ct);
        await _membershipService.EnsureCanWriteAsync(song.ChoirId, ct);
        await EnsureManagerAsync(song.ChoirId, ct);

        _mapper.Map(model, song);
        SyncSongVoicePart(song, model.VoiceParts);
        await _context.SaveChangesAsync(ct);
        return _mapper.Map<SongViewModel>(song);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var song = await _context.Songs
            .Include(c => c.SongVoicePart)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException($"Song {id} not found.");

        await _membershipService.EnsureMemberActiveAsync(song.ChoirId, ct);
        await _membershipService.EnsureCanWriteAsync(song.ChoirId, ct);

        // `02` § Matrice : archive un chant est ouvert au Responsable, et au chef de
        // pupitre pour les chants LIES A SA VOIX. Avant cette garde, un chef de pupitre —
        // ou n'importe quel membre — pouvait archive tout le repertoire, y compris celui
        // d'une chorale ou il n'etait que chanteur.
        if (!await IsManagerAsync(song.ChoirId, ct)
            && !await IsLeaderOfTargetVoicePartAsync(song, ct))
            throw new CustomException(HttpStatusCode.Forbidden,
                "Archiver un chant est réservé au chef de chœur, ou au chef de pupitre d'une voix concernée.");

        song.IsDeleted = true;
        await _context.SaveChangesAsync(ct);
    }

    private static void ApplyCompleteness(Song song, SongViewModel viewModel)
    {
        var scorePublished = song.Scores
            .Any(p => !p.IsDeleted && p.Status == ScoreStatusEnum.Published);

        var coveredVoiceParts = song.Recordings
            .Where(e => !e.IsDeleted
                && e.Status == RecordingStatusEnum.Published
                && e.Type == RecordingTypeEnum.ByVoicePart
                && e.TargetVoicePart.HasValue)
            .Select(e => e.TargetVoicePart!.Value)
            .ToHashSet();

        viewModel.VoicePartsWithoutPublishedRecording = viewModel.VoiceParts
            .Where(v => !coveredVoiceParts.Contains(v))
            .ToList();

        viewModel.IsCompleteForChoir = scorePublished && viewModel.VoicePartsWithoutPublishedRecording.Count == 0;
    }

    private void SyncSongVoicePart(Song song, List<VoicePartEnum> voiceParts)
    {
        var desired = voiceParts.Distinct().ToHashSet();
        var current = song.SongVoicePart.Select(cv => cv.VoicePart).ToHashSet();

        foreach (var songVoicePart in song.SongVoicePart.Where(cv => !desired.Contains(cv.VoicePart)).ToList())
            _context.SongVoiceParts.Remove(songVoicePart);

        foreach (var voicePart in desired.Where(v => !current.Contains(v)))
            song.SongVoicePart.Add(new SongVoicePart { Id = ChoraleDbContext.NewIdGuid(), SongId = song.Id, VoicePart = voicePart });
    }

    private async Task<bool> IsManagerAsync(Guid choirId, CancellationToken ct)
        => _choirAuthorization.IsAdmin() || await _choirAuthorization.IsManagerChoirAsync(choirId, ct);

    private async Task EnsureManagerAsync(Guid choirId, CancellationToken ct)
    {
        if (!await IsManagerAsync(choirId, ct))
            throw new CustomException(HttpStatusCode.Forbidden,
                "Créer ou modifier un chant est réservé au chef de chœur de la chorale.");
    }

    /// <summary>
    /// Le chef de pupitre peut archive un chant si l'une de ses voix de chef fait partie
    /// des voix concernées par ce chant (`02` § Matrice).
    /// </summary>
    private async Task<bool> IsLeaderOfTargetVoicePartAsync(Song song, CancellationToken ct)
    {
        var songVoiceParts = song.SongVoicePart.Select(cv => cv.VoicePart).ToList();
        if (songVoiceParts.Count == 0) return false;

        return await _context.Sections
            .AnyAsync(p => p.ChoirId == song.ChoirId
                           && p.SectionLeaderId == _currentUserId
                           && songVoiceParts.Contains(p.VoicePart), ct);
    }

    /// <summary>
    /// Borne une requête de liste aux chorales dont l'utilisateur peut lire le contenu.
    /// </summary>
    private async Task<IQueryable<Song>> RestrictToAccessibleChoirsAsync(
        IQueryable<Song> query, CancellationToken ct)
    {
        if (_currentUserRoles.Contains(UserRoleEnum.Admin)) return query;

        var accessibles = await _membershipService.ChoirsAccessibleAsync(ct);
        return query.Where(c => accessibles.Contains(c.ChoirId));
    }
}
