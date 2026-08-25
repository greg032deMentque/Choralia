using System.Linq.Expressions;
using System.Net;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Helpers;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.SongLists;
using Microsoft.EntityFrameworkCore;

namespace ChoraleBackEnd.Services.ChoirServices;

public interface ISongListService
{
    Task<PagedListViewModel<SongListViewModel>> GetPagedAsync(SongListPagedFilterViewModel filter, CancellationToken ct = default);
    Task<SongListViewModel> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<SongListViewModel> CreateAsync(SongListViewModel model, CancellationToken ct = default);
    Task<SongListViewModel> UpdateAsync(SongListViewModel model, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<SongListViewModel> AddSongAsync(Guid songListId, AddSongViewModel model, CancellationToken ct = default);
    Task RemoveSongAsync(Guid songListId, Guid songId, CancellationToken ct = default);
    Task<SongListViewModel> ReorderSongsAsync(Guid songListId, ReorderSongsViewModel model, CancellationToken ct = default);
    Task<SongListViewModel> PublishAsync(Guid id, CancellationToken ct = default);
    Task<SongListViewModel> ArchiveAsync(Guid id, CancellationToken ct = default);
    Task<SongListViewModel> RevertToDraftAsync(Guid id, CancellationToken ct = default);
}

public sealed class SongListService : BaseService, ISongListService
{
    // Liste blanche de tri : sans elle, Skip/Take sur une requete non triee n'est pas
    // reproductible — voir TriHelper. Tri par defaut par Nom, departage sur Id.
    private static readonly IReadOnlyDictionary<string, Expression<Func<SongList, object?>>> ListsSortableColumns =
        new Dictionary<string, Expression<Func<SongList, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = d => d.Name,
            ["Status"] = d => d.Status,
            ["Type"] = d => d.Type,
            ["CreatedAt"] = d => d.CreatedAt
        };

    private readonly IMembershipService _membershipService;
    private readonly IChoirAuthorizationService _choirAuthorization;

    public SongListService(
        IServiceProvider serviceProvider,
        IMembershipService membershipService,
        IChoirAuthorizationService choirAuthorization)
        : base(serviceProvider)
    {
        _membershipService = membershipService;
        _choirAuthorization = choirAuthorization;
    }

    public async Task<PagedListViewModel<SongListViewModel>> GetPagedAsync(
        SongListPagedFilterViewModel filter, CancellationToken ct = default)
    {
        var isAdmin = _choirAuthorization.IsAdmin();

        var query = _context.SongLists
            .AsNoTracking()
            .AsQueryable();

        if (!isAdmin)
        {
            query = query.Where(d =>
                d.ChoirId.HasValue && _context.SpaceMembers
                    .Any(m => m.ChoirId == d.ChoirId && m.UserId == _currentUserId) ||
                d.SectionId.HasValue && _context.SectionMembers
                    .Any(m => m.SectionId == d.SectionId && m.UserId == _currentUserId));
        }

        if (filter.EventId.HasValue)
            query = query.Where(d => d.EventId == filter.EventId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Filter))
            query = query.Where(d => d.Name.Contains(filter.Filter));

        var total = await query.CountAsync(ct);
        var items = await query
            .ApplySort(
                filter.SortActive, filter.SortDirection, ListsSortableColumns, d => d.Id,
                q => q.OrderBy(d => d.Name).ThenBy(d => d.Id))
            .Skip(filter.Offset)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return new PagedListViewModel<SongListViewModel>
        {
            Items = _mapper.Map<List<SongListViewModel>>(items),
            TotalCount = total,
            CurrentPage = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<SongListViewModel> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var songList = await _context.SongLists
            .AsNoTracking()
            .Include(d => d.SongListSongs)
                .ThenInclude(dc => dc.Song)
            .FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new KeyNotFoundException($"SongList {id} not found.");

        await EnsureAccesSongListAsync(songList, ct);

        var vm = _mapper.Map<SongListViewModel>(songList);
        vm.Songs = _mapper.Map<List<SongListSongViewModel>>(songList.SongListSongs);
        return vm;
    }

    public async Task<SongListViewModel> CreateAsync(SongListViewModel model, CancellationToken ct = default)
    {
        var choirAutoDerivee = await ValidateMembershipAsync(model, ct);
        var choirTarget = model.ChoirId ?? choirAutoDerivee;
        await EnsureCreationMembershipAsync(choirTarget, model.SectionId, ct);
        await EnsureWriteChoirAsync(choirTarget, model.SectionId, ct);

        var songList = _mapper.Map<SongList>(model);
        songList.Id = ChoraleDbContext.NewIdGuid();
        // Le profil ignore les trois cles de rattachement : c'est ici qu'elles se posent,
        // apres ValidateMembershipAsync (coherence) et EnsureCreationMembershipAsync
        // (appartenance a la cible).
        songList.ChoirId = model.ChoirId;
        songList.SectionId = model.SectionId;
        songList.EventId = model.EventId;
        songList.CreatedById = _currentUserId;
        songList.Status = SongListStatusEnum.Draft;
        songList.OwnerUserId = _currentUserId
            ?? throw new CustomException(HttpStatusCode.Unauthorized, "Utilisateur non authentifié.");

        if (choirAutoDerivee.HasValue && !songList.ChoirId.HasValue)
            songList.ChoirId = choirAutoDerivee;

        _context.SongLists.Add(songList);
        await _context.SaveChangesAsync(ct);
        return _mapper.Map<SongListViewModel>(songList);
    }

    public async Task<SongListViewModel> UpdateAsync(SongListViewModel model, CancellationToken ct = default)
    {
        if (model.Id is null)
            throw new CustomException(HttpStatusCode.BadRequest, "Id requis.");

        // La chorale derivee de l'evenement n'est plus exploitee ici : le rattachement etant
        // immuable en update, il n'y a plus rien a combler. L'appel subsiste pour ses
        // controles de coherence sur le corps de la requete.
        _ = await ValidateMembershipAsync(model, ct);

        var songList = await _context.SongLists
            .FirstOrDefaultAsync(d => d.Id == model.Id, ct)
            ?? throw new KeyNotFoundException($"SongList {model.Id} not found.");

        await EnsureModificationAsync(songList, ct);
        await EnsureWriteChoirAsync(songList.ChoirId, songList.SectionId, ct);
        EnsureTypeMatchesStoredScope(model.Type, songList);

        _mapper.Map(model, songList);

        await _context.SaveChangesAsync(ct);
        return _mapper.Map<SongListViewModel>(songList);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var songList = await _context.SongLists
            .FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new KeyNotFoundException($"SongList {id} not found.");

        await EnsureModificationAsync(songList, ct);
        await EnsureWriteChoirAsync(songList.ChoirId, songList.SectionId, ct);

        songList.IsDeleted = true;
        await _context.SaveChangesAsync(ct);
    }

    public async Task<SongListViewModel> AddSongAsync(
        Guid songListId, AddSongViewModel model, CancellationToken ct = default)
    {
        var songList = await _context.SongLists
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == songListId, ct)
            ?? throw new KeyNotFoundException($"SongList {songListId} not found.");

        await EnsureModificationAsync(songList, ct);
        await EnsureWriteChoirAsync(songList.ChoirId, songList.SectionId, ct);
        await EnsureSongCongruentAsync(songList, model.SongId, ct);
        EnsureCompositionModifiable(songList);

        var exists = await _context.SongListSongs
            .AnyAsync(dc => dc.SongListId == songListId && dc.SongId == model.SongId, ct);

        if (!exists)
        {
            _context.SongListSongs.Add(new SongListSong
            {
                Id = ChoraleDbContext.NewIdGuid(),
                SongListId = songListId,
                SongId = model.SongId,
                Position = model.Position
            });
            await _context.SaveChangesAsync(ct);
        }

        return await GetByIdAsync(songListId, ct);
    }

    public async Task RemoveSongAsync(Guid songListId, Guid songId, CancellationToken ct = default)
    {
        var songList = await _context.SongLists
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == songListId, ct)
            ?? throw new KeyNotFoundException($"SongList {songListId} not found.");

        await EnsureModificationAsync(songList, ct);
        await EnsureWriteChoirAsync(songList.ChoirId, songList.SectionId, ct);
        EnsureCompositionModifiable(songList);

        var lien = await _context.SongListSongs
            .FirstOrDefaultAsync(dc => dc.SongListId == songListId && dc.SongId == songId, ct)
            ?? throw new KeyNotFoundException("Song not found in this song list.");

        _context.SongListSongs.Remove(lien);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<SongListViewModel> ReorderSongsAsync(
        Guid songListId, ReorderSongsViewModel model, CancellationToken ct = default)
    {
        var songList = await _context.SongLists
            .Include(d => d.SongListSongs)
            .FirstOrDefaultAsync(d => d.Id == songListId, ct)
            ?? throw new KeyNotFoundException($"SongList {songListId} not found.");

        await EnsureModificationAsync(songList, ct);
        await EnsureWriteChoirAsync(songList.ChoirId, songList.SectionId, ct);

        if (songList.Status != SongListStatusEnum.Draft)
            throw new CustomException(HttpStatusCode.Conflict,
                "Seule une liste en brouillon peut être réordonnée.");

        var songIdsExistants = songList.SongListSongs.Select(dc => dc.SongId).ToHashSet();

        if (model.SongIds.Count != songIdsExistants.Count
            || model.SongIds.Distinct().Count() != model.SongIds.Count
            || !model.SongIds.All(songIdsExistants.Contains))
            throw new CustomException(HttpStatusCode.BadRequest,
                "La liste de chants fournie ne correspond pas à la composition actuelle de la liste.");

        for (var position = 0; position < model.SongIds.Count; position++)
        {
            var songListSong = songList.SongListSongs.First(dc => dc.SongId == model.SongIds[position]);
            songListSong.Position = position;
        }

        await _context.SaveChangesAsync(ct);
        return await GetByIdAsync(songListId, ct);
    }

    public async Task<SongListViewModel> PublishAsync(Guid id, CancellationToken ct = default)
    {
        var songList = await _context.SongLists
            .FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new KeyNotFoundException($"SongList {id} not found.");

        await EnsurePublicationRightsAsync(songList, ct);
        await EnsureWriteChoirAsync(songList.ChoirId, songList.SectionId, ct);

        if (songList.Status != SongListStatusEnum.Draft)
            throw new CustomException(HttpStatusCode.Conflict, "Seule une liste en brouillon peut être publiée.");

        songList.Status = SongListStatusEnum.Published;
        await _context.SaveChangesAsync(ct);
        return _mapper.Map<SongListViewModel>(songList);
    }

    public async Task<SongListViewModel> ArchiveAsync(Guid id, CancellationToken ct = default)
    {
        var songList = await _context.SongLists
            .FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new KeyNotFoundException($"SongList {id} not found.");

        await EnsurePublicationRightsAsync(songList, ct);
        await EnsureWriteChoirAsync(songList.ChoirId, songList.SectionId, ct);

        if (songList.Status == SongListStatusEnum.Archived)
            throw new CustomException(HttpStatusCode.Conflict, "Cette liste est déjà archivée.");

        songList.Status = SongListStatusEnum.Archived;
        await _context.SaveChangesAsync(ct);
        return _mapper.Map<SongListViewModel>(songList);
    }

    public async Task<SongListViewModel> RevertToDraftAsync(Guid id, CancellationToken ct = default)
    {
        var songList = await _context.SongLists
            .FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new KeyNotFoundException($"SongList {id} not found.");

        await EnsurePublicationRightsAsync(songList, ct);
        await EnsureWriteChoirAsync(songList.ChoirId, songList.SectionId, ct);

        if (songList.Status != SongListStatusEnum.Published)
            throw new CustomException(HttpStatusCode.Conflict, "Seule une liste publiée peut repasser en brouillon.");

        songList.Status = SongListStatusEnum.Draft;
        await _context.SaveChangesAsync(ct);
        return _mapper.Map<SongListViewModel>(songList);
    }

    private async Task<Guid?> ValidateMembershipAsync(SongListViewModel model, CancellationToken ct)
    {
        if (model.Type == SongListTypeEnum.Event)
        {
            if (!model.EventId.HasValue)
                throw new CustomException(HttpStatusCode.BadRequest,
                    "Une liste de type Événement doit référencer un événement.");

            if (model.SectionId.HasValue)
                throw new CustomException(HttpStatusCode.BadRequest,
                    "Une liste de type Événement ne peut pas appartenir à un pupitre.");

            var evt = await _context.Events
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == model.EventId, ct)
                ?? throw new KeyNotFoundException($"Event {model.EventId} not found.");

            if (model.ChoirId.HasValue && model.ChoirId.Value != evt.ChoirId)
                throw new CustomException(HttpStatusCode.BadRequest,
                    "La liste doit appartenir à la même chorale que l'événement.");

            return evt.ChoirId;
        }

        if (model.EventId.HasValue)
            throw new CustomException(HttpStatusCode.BadRequest,
                "Seule une liste de type Événement peut référencer un événement.");

        var hasChoirId = model.ChoirId.HasValue;
        var hasSectionId = model.SectionId.HasValue;

        if (!hasChoirId && !hasSectionId)
            throw new CustomException(HttpStatusCode.BadRequest,
                "Une liste de chants doit appartenir à une chorale ou à un pupitre.");

        if (hasChoirId && hasSectionId)
            throw new CustomException(HttpStatusCode.BadRequest,
                "Une liste de chants ne peut pas appartenir à la fois à une chorale et à un pupitre.");

        return null;
    }

    /// <summary>
    /// Verifie que le type demande correspond au rattachement DEJA STOCKE.
    /// </summary>
    /// <remarks>
    /// Depuis que les cles de rattachement ne sont plus mappables en update (voir
    /// <c>SongListViewModelMappingProfile</c>), <see cref="ValidateMembershipAsync"/> ne
    /// suffit plus : elle raisonne sur le corps de la requete, dont les cles ne seront pas
    /// ecrites. Sans ce controle, poser <c>Type = Event</c> sur une liste sans evenement
    /// stocke passerait la validation et produirait une ligne incoherente.
    /// </remarks>
    private static void EnsureTypeMatchesStoredScope(SongListTypeEnum type, SongList songList)
    {
        if (type == SongListTypeEnum.Event && !songList.EventId.HasValue)
            throw new CustomException(HttpStatusCode.BadRequest,
                "Cette liste n'est rattachée à aucun événement : son type ne peut pas être « Événement ».");

        if (type != SongListTypeEnum.Event && songList.EventId.HasValue)
            throw new CustomException(HttpStatusCode.BadRequest,
                "Cette liste est rattachée à un événement : son type doit rester « Événement ».");
    }

    private async Task EnsureCreationMembershipAsync(Guid? choirId, Guid? sectionId, CancellationToken ct)
    {
        if (_choirAuthorization.IsAdmin()) return;

        if (choirId.HasValue)
        {
            var isMember = await _context.SpaceMembers
                .AnyAsync(m => m.ChoirId == choirId.Value && m.UserId == _currentUserId, ct);
            if (isMember) return;

            throw new CustomException(HttpStatusCode.Forbidden,
                "Vous devez être membre de cette chorale pour y créer une liste.");
        }

        if (sectionId.HasValue)
        {
            var isMember = await _context.SectionMembers
                .AnyAsync(m => m.SectionId == sectionId.Value && m.UserId == _currentUserId, ct);
            if (isMember) return;

            var isLeader = await _context.Sections
                .AnyAsync(p => p.Id == sectionId.Value && p.SectionLeaderId == _currentUserId, ct);
            if (isLeader) return;

            throw new CustomException(HttpStatusCode.Forbidden,
                "Vous devez être membre (ou chef) de ce pupitre pour y créer une liste.");
        }

        throw new CustomException(HttpStatusCode.Conflict, "Impossible de déterminer l'appartenance de cette liste.");
    }

    /// <summary>
    /// Accès à une liste de chants, selon son rattachement.
    /// </summary>
    /// <remarks>
    /// Ce contrôle était construit en autorisations successives, sans refus terminal : une
    /// liste sans chorale <b>ni</b> pupitre sortait de la méthode en étant autorisée, donc
    /// lisible et modifiable par n'importe quel compte. Le chemin de création actuel empêche
    /// cet état, mais rien ne le garantissait dans le temps.
    /// </remarks>
    private async Task EnsureAccesSongListAsync(SongList songList, CancellationToken ct)
    {
        if (_choirAuthorization.IsAdmin()) return;

        if (songList.ChoirId.HasValue)
        {
            await _membershipService.EnsureMemberActiveAsync(songList.ChoirId.Value, ct);
            return;
        }

        if (songList.SectionId.HasValue)
        {
            var isMember = await _context.SectionMembers
                .AnyAsync(m => m.SectionId == songList.SectionId && m.UserId == _currentUserId, ct);
            if (isMember) return;
        }

        // Refus terminal : aucun rattachement reconnu ne vaut pas autorisation.
        throw new CustomException(HttpStatusCode.Forbidden, "Accès refusé à cette liste de chants.");
    }

    /// <summary>
    /// Un chant ne rejoint une liste que s'il appartient à la même chorale qu'elle.
    /// </summary>
    /// <remarks>
    /// Sans ce contrôle, il suffisait de créer une liste dans sa propre chorale puis d'y
    /// attacher l'identifiant d'un chant d'une autre chorale — voire d'un autre client — pour
    /// en lire le titre via le détail de la liste. Le canal de fuite survivait à la
    /// correction des endpoints de liste, puisqu'il passe par une opération d'écriture.
    /// </remarks>
    private async Task EnsureSongCongruentAsync(
        SongList songList, Guid songId, CancellationToken ct)
    {
        var songChoirId = await _context.Songs
            .AsNoTracking()
            .Where(c => c.Id == songId)
            .Select(c => (Guid?)c.ChoirId)
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"Song {songId} not found.");

        var songListChoirId = songList.ChoirId
            ?? await _context.Sections
                .AsNoTracking()
                .Where(p => p.Id == songList.SectionId)
                .Select(p => (Guid?)p.ChoirId)
                .FirstOrDefaultAsync(ct);

        if (songListChoirId != songChoirId)
            throw new CustomException(HttpStatusCode.Forbidden,
                "Ce chant n'appartient pas à la chorale de cette liste.");
    }

    private async Task EnsureModificationAsync(SongList songList, CancellationToken ct)
    {
        if (_choirAuthorization.IsAdmin()) return;

        var isCreator = songList.CreatedById == _currentUserId;
        if (isCreator) return;

        if (songList.SectionId.HasValue)
        {
            var isLeader = await _context.Sections
                .AnyAsync(p => p.Id == songList.SectionId && p.SectionLeaderId == _currentUserId, ct);
            if (isLeader) return;
        }

        throw new CustomException(HttpStatusCode.Forbidden,
            "Seul le créateur, un admin ou un chef de pupitre peut modifier cette liste de chants.");
    }

    /// <summary>
    /// Ferme l'ecriture sur une liste des lors que la chorale qui la porte (directement, ou
    /// via son pupitre) n'accepte plus le contenu — voir
    /// <see cref="IMembershipService.EnsureCanWriteAsync"/>. Une liste sans rattachement
    /// resoluble (evenement autonome, par exemple) n'est pas concernee : il n'existe alors
    /// aucune chorale dont le statut pourrait fermer l'ecriture.
    /// </summary>
    private async Task EnsureWriteChoirAsync(Guid? choirId, Guid? sectionId, CancellationToken ct)
    {
        var choirTarget = choirId;

        if (!choirTarget.HasValue && sectionId.HasValue)
        {
            choirTarget = await _context.Sections
                .AsNoTracking()
                .Where(p => p.Id == sectionId.Value)
                .Select(p => (Guid?)p.ChoirId)
                .FirstOrDefaultAsync(ct);
        }

        if (choirTarget.HasValue)
            await _membershipService.EnsureCanWriteAsync(choirTarget.Value, ct);
    }

    private static void EnsureCompositionModifiable(SongList songList)
    {
        if (songList.Status == SongListStatusEnum.Published)
            throw new CustomException(HttpStatusCode.Conflict,
                "La liste est publiée : repassez-la en brouillon avant de modifier sa composition.");
    }

    private async Task<Guid> ResolveChoirIdAsync(SongList songList, CancellationToken ct)
    {
        if (songList.ChoirId.HasValue)
            return songList.ChoirId.Value;

        if (songList.SectionId.HasValue)
        {
            var choirId = await _context.Sections
                .AsNoTracking()
                .Where(p => p.Id == songList.SectionId)
                .Select(p => (Guid?)p.ChoirId)
                .FirstOrDefaultAsync(ct);

            if (choirId.HasValue)
                return choirId.Value;
        }

        throw new CustomException(HttpStatusCode.Conflict, "Impossible de déterminer la chorale de cette liste.");
    }

    private async Task EnsurePublicationRightsAsync(SongList songList, CancellationToken ct)
    {
        if (_choirAuthorization.IsAdmin()) return;

        var choirId = await ResolveChoirIdAsync(songList, ct);
        if (await _choirAuthorization.IsManagerChoirAsync(choirId, ct)) return;

        if (songList.Type == SongListTypeEnum.Section && songList.SectionId.HasValue)
        {
            var isSectionLeader = await _context.Sections
                .AnyAsync(p => p.Id == songList.SectionId && p.SectionLeaderId == _currentUserId, ct);
            if (isSectionLeader) return;
        }

        throw new CustomException(HttpStatusCode.Forbidden,
            "Seul un chef de chœur (ou le chef du pupitre concerné pour une liste de pupitre) peut gérer la publication de cette liste.");
    }
}
