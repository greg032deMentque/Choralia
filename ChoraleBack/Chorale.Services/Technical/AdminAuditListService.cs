using System.Linq.Expressions;
using ChoraleBackEnd.Common.Helpers;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.AdminAudit;
using Microsoft.EntityFrameworkCore;

namespace ChoraleBackEnd.Services.Technical;

/// <summary>
/// Ecran d'audit de l'administration generale — lecture seule. <c>AdminAuditLog</c> est
/// alimente depuis le lot 3 par <see cref="IAuditLogService"/>, mais rien ne l'affichait
/// jusqu'ici : on tracait dans le vide.
/// </summary>
/// <remarks>
/// Aucune ecriture n'est exposee : un journal d'audit modifiable ne vaut rien. Le nom de
/// l'acteur est enrichi apres pagination, en une requete groupee par page (jamais un acces au
/// contexte par ligne) — <c>UserId</c> seul n'est pas exploitable a l'ecran.
/// </remarks>
public interface IAdminAuditListService
{
    Task<PagedListViewModel<AdminAuditLogListItemViewModel>> GetPagedAsync(
        AdminAuditLogPagedFilterViewModel filter, CancellationToken ct = default);
}

public sealed class AdminAuditListService : BaseService, IAdminAuditListService
{
    private const string ActeurInconnu = "Utilisateur inconnu";

    private static readonly IReadOnlyDictionary<string, Expression<Func<AdminAuditLog, object?>>> AuditSortableColumns =
        new Dictionary<string, Expression<Func<AdminAuditLog, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["OccurredAt"] = a => a.OccurredAt,
            ["Action"] = a => a.Action,
            ["EntityType"] = a => a.EntityType
        };

    public AdminAuditListService(IServiceProvider serviceProvider) : base(serviceProvider) { }

    public async Task<PagedListViewModel<AdminAuditLogListItemViewModel>> GetPagedAsync(
        AdminAuditLogPagedFilterViewModel filter, CancellationToken ct = default)
    {
        // Periode inversee : un result vide est la seule reponse sensee, jamais une
        // exception qui exposerait un detail technique au client HTTP.
        if (filter.StartDate.HasValue && filter.EndDate.HasValue && filter.StartDate > filter.EndDate)
            return new PagedListViewModel<AdminAuditLogListItemViewModel>
            {
                Items = [],
                TotalCount = 0,
                CurrentPage = filter.Page,
                PageSize = filter.PageSize
            };

        var query = _context.AdminAuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.UserId))
            query = query.Where(a => a.UserId == filter.UserId);

        if (!string.IsNullOrWhiteSpace(filter.EntityType))
            query = query.Where(a => a.EntityType == filter.EntityType);

        if (!string.IsNullOrWhiteSpace(filter.Action))
            query = query.Where(a => a.Action == filter.Action);

        if (filter.StartDate.HasValue)
            query = query.Where(a => a.OccurredAt >= filter.StartDate.Value);

        if (filter.EndDate.HasValue)
            query = query.Where(a => a.OccurredAt <= filter.EndDate.Value);

        var total = await query.CountAsync(ct);
        var items = await query
            .ApplySort(
                filter.SortActive, filter.SortDirection, AuditSortableColumns, a => a.Id,
                q => q.OrderByDescending(a => a.OccurredAt).ThenBy(a => a.Id))
            .Skip(filter.Offset)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        var viewModels = _mapper.Map<List<AdminAuditLogListItemViewModel>>(items);
        await EnrichActeursAsync(viewModels, ct);

        return new PagedListViewModel<AdminAuditLogListItemViewModel>
        {
            Items = viewModels,
            TotalCount = total,
            CurrentPage = filter.Page,
            PageSize = filter.PageSize
        };
    }

    /// <summary>
    /// Une seule requete groupee par page. <c>IgnoreQueryFilters</c> est deliberement present :
    /// un acteur soft-delete (compte anonymise ou supprime) doit rester resolvable — l'audit
    /// survit a la suppression de l'entite qu'il trace. Un <c>UserId</c> introuvable (action
    /// systeme, ou compte reellement disparu) retombe sur un libelle lisible plutot qu'un vide.
    /// </summary>
    private async Task EnrichActeursAsync(List<AdminAuditLogListItemViewModel> viewModels, CancellationToken ct)
    {
        if (viewModels.Count == 0) return;

        var userIds = viewModels.Select(v => v.UserId).Distinct().ToList();

        var acteurs = await _context.Users
            .IgnoreQueryFilters()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Firstname, u.Lastname, u.Email })
            .ToDictionaryAsync(u => u.Id, u => u, ct);

        foreach (var viewModel in viewModels)
        {
            if (!acteurs.TryGetValue(viewModel.UserId, out var acteur))
            {
                viewModel.UserFullName = ActeurInconnu;
                continue;
            }

            var fullName = $"{acteur.Firstname} {acteur.Lastname}".Trim();
            viewModel.UserFullName = string.IsNullOrWhiteSpace(fullName) ? ActeurInconnu : fullName;
            viewModel.UserEmail = acteur.Email;
        }
    }
}
