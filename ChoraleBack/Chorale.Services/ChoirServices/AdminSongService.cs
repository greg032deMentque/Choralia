using System.Linq.Expressions;
using System.Net;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Helpers;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.AdminSongs;
using Microsoft.EntityFrameworkCore;

namespace ChoraleBackEnd.Services.ChoirServices;

/// <summary>
/// Catalogue transverse des chants pour l'administration generale (lot 4). Chaque chorale
/// cree ses propres <c>Song</c> (entite portant un <c>ChoirId</c> obligatoire) : le meme
/// chant depose par sept chorales produit sept lignes en base. Ce service ne fusionne rien
/// en base — decision produit explicite : aucune entite <c>Oeuvre</c>, aucune migration,
/// aucun rattachement retroactif — il regroupe uniquement a l'AFFICHAGE, via
/// <see cref="SongKeyHelper"/>.
/// </summary>
/// <remarks>
/// Lecture seule et transverse a tous les clients, exactement comme <c>AdminChoirService</c>.
/// Aucun acces au contenu (partitions, enregistrements) et aucune ecriture : l'admin voit le
/// catalogue, il n'entre pas dans le contenu (decision produit).
///
/// La cle de regroupement ne peut pas etre calculee par une requete SQL traduisible (elle
/// depend de <c>ToLowerInvariant</c> et d'une normalisation Unicode explicite, jamais de la
/// collation du serveur — voir <see cref="SongKeyHelper"/>) : les chants non archives sont
/// donc materialises en une seule requete groupee (pas d'acces au contexte dans une boucle,
/// pas de N+1), puis regroupes et pagines en memoire. Ce choix a un cout — l'ensemble des
/// chants actifs est charge a chaque appel — assume pour l'echelle actuelle de l'application
/// (une chorale ne depasse pas quelques centaines de chants).
/// </remarks>
public interface IAdminSongService
{
    Task<PagedListViewModel<AdminSongCatalogItemViewModel>> GetPagedCatalogueAsync(
        AdminSongCatalogPagedFilterViewModel filter, CancellationToken ct = default);

    Task<List<AdminSongGroupChoirItemViewModel>> GetGroupChoirsAsync(
        string key, CancellationToken ct = default);
}

public sealed class AdminSongService : BaseService, IAdminSongService
{
    // Liste blanche de tri : ChoirCount EST triable ici (a la difference du modele
    // habituel ClientService/AdminChoirService, ou les agregats sont calcules APRES
    // pagination). Le regroupement complet a deja eu lieu en memoire avant tout tri : trier
    // sur ChoirCount ne demande donc aucune restructuration de requete.
    private static readonly IReadOnlyDictionary<string, Expression<Func<GroupSong, object?>>> GroupsSortableColumns =
        new Dictionary<string, Expression<Func<GroupSong, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Title"] = g => g.Title,
            ["Composer"] = g => g.Composer,
            ["ChoirCount"] = g => g.ChoirCount
        };

    public AdminSongService(IServiceProvider serviceProvider) : base(serviceProvider) { }

    public async Task<PagedListViewModel<AdminSongCatalogItemViewModel>> GetPagedCatalogueAsync(
        AdminSongCatalogPagedFilterViewModel filter, CancellationToken ct = default)
    {
        var rows = await LoadRowsAsync(ct);
        var groupes = Group(rows);

        if (!string.IsNullOrWhiteSpace(filter.Filter))
        {
            var recherche = filter.Filter.Trim();
            groupes = groupes.Where(g =>
                    g.Title.Contains(recherche, StringComparison.OrdinalIgnoreCase)
                    || g.Composer != null && g.Composer.Contains(recherche, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (filter.DuplicatesOnly == true)
            groupes = groupes.Where(g => g.ChoirCount > 1).ToList();

        var total = groupes.Count;

        // Pagination SUR LES GROUPES : le regroupement (Group) a deja eu lieu avant ce
        // point, donc un groupe ne peut jamais se retrouver coupe entre deux pages. ThenBy(Cle)
        // departage a egalite, sinon Skip/Take ne serait pas reproductible.
        var items = groupes
            .AsQueryable()
            .ApplySort(
                filter.SortActive, filter.SortDirection, GroupsSortableColumns, g => g.Key,
                q => q.OrderBy(g => g.Title).ThenBy(g => g.Key))
            .Skip(filter.Offset)
            .Take(filter.PageSize)
            .ToList();

        return new PagedListViewModel<AdminSongCatalogItemViewModel>
        {
            Items = items.Select(g => new AdminSongCatalogItemViewModel
            {
                Key = g.Key,
                Title = g.Title,
                Composer = g.Composer,
                ChoirCount = g.ChoirCount,
                OccurrenceCount = g.OccurrenceCount
            }).ToList(),
            TotalCount = total,
            CurrentPage = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<List<AdminSongGroupChoirItemViewModel>> GetGroupChoirsAsync(
        string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new CustomException(HttpStatusCode.BadRequest, "Clé de groupe requise.");

        var rows = await LoadRowsAsync(ct);

        return rows
            .Where(l => SongKeyHelper.ComputeKey(l.SongId, l.Title, l.Composer) == key)
            .OrderBy(l => l.ChoirName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.ChoirId)
            .Select(l => new AdminSongGroupChoirItemViewModel
            {
                ChoirId = l.ChoirId,
                ChoirName = l.ChoirName,
                ClientName = l.ClientName,
                SongStatus = l.Status,
                CreationDate = l.CreatedAt
            })
            .ToList();
    }

    /// <summary>
    /// Une seule requete groupee (jointures Choir/Client via navigation, traduites en SQL)
    /// pour tout le catalogue — jamais d'acces au contexte a l'interieur d'une boucle. Chant
    /// soft-delete deja exclu par le filtre de requete par defaut ; chorale <c>Archive</c>
    /// exclue explicitement (elle ne porte pas <c>IsDeleted</c>, migration 13). Un client
    /// suspendu reste INCLUS : voir le chant d'un client suspendu, c'est le metier de l'admin.
    /// </summary>
    private Task<List<SongCatalogueRow>> LoadRowsAsync(CancellationToken ct)
        => _context.Songs
            .AsNoTracking()
            .Where(c => c.Choir.Status != ChoirStatusEnum.Archived)
            .Select(c => new SongCatalogueRow
            {
                SongId = c.Id,
                Title = c.Title,
                Composer = c.Composer,
                ChoirId = c.ChoirId,
                ChoirName = c.Choir.Name,
                ClientName = c.Choir.Client.Name,
                Status = c.Status,
                CreatedAt = c.CreatedAt
            })
            .ToListAsync(ct);

    private static List<GroupSong> Group(List<SongCatalogueRow> rows)
        => rows
            .GroupBy(l => SongKeyHelper.ComputeKey(l.SongId, l.Title, l.Composer))
            .Select(g =>
            {
                // Titre/composer de reference : le plus frequent dans le groupe, ou le
                // premier par ordre alphabetique en cas d'egalite — decision assumee, la
                // spec ne tranchait pas ce choix.
                var reference = g
                    .GroupBy(l => (l.Title, l.Composer))
                    .OrderByDescending(rg => rg.Count())
                    .ThenBy(rg => rg.Key.Title, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(rg => rg.Key.Composer, StringComparer.OrdinalIgnoreCase)
                    .First().Key;

                return new GroupSong
                {
                    Key = g.Key,
                    Title = reference.Title,
                    Composer = reference.Composer,
                    ChoirCount = g.Select(l => l.ChoirId).Distinct().Count(),
                    OccurrenceCount = g.Count()
                };
            })
            .ToList();

    private sealed class SongCatalogueRow
    {
        public Guid SongId { get; init; }
        public string Title { get; init; } = string.Empty;
        public string? Composer { get; init; }
        public Guid ChoirId { get; init; }
        public string ChoirName { get; init; } = string.Empty;
        public string ClientName { get; init; } = string.Empty;
        public SongStatusEnum Status { get; init; }
        public DateTime CreatedAt { get; init; }
    }

    private sealed class GroupSong
    {
        public string Key { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string? Composer { get; init; }
        public int ChoirCount { get; init; }
        public int OccurrenceCount { get; init; }
    }
}
