using System.Linq.Expressions;
using System.Net;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Helpers;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.ViewModels;
using Microsoft.EntityFrameworkCore;
using ChoraleBackEnd.ViewModels.Users;

namespace ChoraleBackEnd.Services.UserServices;

public interface ISingerService
{
    Task<PagedListViewModel<UserViewModel>> GetPagedAsync(PaginateViewModel pagination, CancellationToken ct = default);
    Task<UserViewModel> GetByIdAsync(string id, CancellationToken ct = default);
    Task<UserViewModel> CreateAsync(UserViewModel model, CancellationToken ct = default);
    Task<UserViewModel> UpdateAsync(UserViewModel model, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
}

public sealed class SingerService : BaseService, ISingerService
{
    // Liste blanche de tri : sans elle, Skip/Take sur une requete non triee n'est pas
    // reproductible (deux pages consecutives peuvent se recouvrir ou perdre des lignes) — voir
    // TriHelper. Tri par defaut par Lastname/Firstname, departage sur Id.
    private static readonly IReadOnlyDictionary<string, Expression<Func<User, object?>>> SingersSortableColumns =
        new Dictionary<string, Expression<Func<User, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Lastname"] = u => u.Lastname,
            ["Firstname"] = u => u.Firstname,
            ["Email"] = u => u.Email,
            ["IsActive"] = u => u.IsActive,
            ["CreatedAt"] = u => u.CreatedAt
        };

    private readonly IAuditLogService _auditLogService;

    public SingerService(IServiceProvider serviceProvider, IAuditLogService auditLogService)
        : base(serviceProvider)
    {
        _auditLogService = auditLogService;
    }

    public async Task<PagedListViewModel<UserViewModel>> GetPagedAsync(
        PaginateViewModel pagination, CancellationToken ct = default)
    {
        var query = _userManager.Users
            .Where(u => !u.IsDeleted)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(pagination.Filter))
            query = query.Where(u =>
                u.Firstname.Contains(pagination.Filter) ||
                u.Lastname.Contains(pagination.Filter) ||
                u.Email != null && u.Email.Contains(pagination.Filter));

        var singerIds = await (
            from u in query
            join ur in _context.UserRoles on u.Id equals ur.UserId
            join r in _context.Roles on ur.RoleId equals r.Id
            where r.Name == UserRoleEnum.Singer.ToString()
            select u.Id
        ).ToListAsync(ct);

        var singers = query.Where(u => singerIds.Contains(u.Id));

        var total = await singers.CountAsync(ct);
        var items = await singers
            .ApplySort(
                pagination.SortActive, pagination.SortDirection, SingersSortableColumns, u => u.Id,
                q => q.OrderBy(u => u.Lastname).ThenBy(u => u.Firstname).ThenBy(u => u.Id))
            .Skip(pagination.Offset)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        var viewModels = _mapper.Map<List<UserViewModel>>(items);

        // Roles peuplés en une requête groupée sur la page. Constaté en exerçant l'API :
        // Create renvoyait ["Chanteur"] et GetPaged renvoyait [] pour le même utilisateur —
        // le mapping ignore Roles et seul Create le repeuplait. Un appelant qui filtre sur
        // les rôles voyait donc deux vérités selon le chemin.
        var idsPage = items.Select(u => u.Id).ToList();
        var rolesByUser = (await (
                from ur in _context.UserRoles
                join r in _context.Roles on ur.RoleId equals r.Id
                where idsPage.Contains(ur.UserId) && r.Name != null
                select new { ur.UserId, r.Name }
            ).ToListAsync(ct))
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name!).ToList());

        foreach (var viewModel in viewModels)
            if (viewModel.Id is { } id && rolesByUser.TryGetValue(id, out var roles))
                viewModel.Roles = roles;

        return new PagedListViewModel<UserViewModel>
        {
            Items = viewModels,
            TotalCount = total,
            CurrentPage = pagination.Page,
            PageSize = pagination.PageSize
        };
    }

    public async Task<UserViewModel> GetByIdAsync(string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new CustomException(HttpStatusCode.BadRequest, "Id requis.");

        var user = await _userManager.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted, ct)
            ?? throw new KeyNotFoundException($"Singer {id} not found.");

        // La surface "singers" ne doit lire/muter que des comptes Singer — sans ce controle,
        // un id valide de compte non-chanteur (ex. Admin) etait lisible/modifiable par ici.
        if (!await _userManager.IsInRoleAsync(user, UserRoleEnum.Singer.ToString()))
            throw new KeyNotFoundException($"Singer {id} not found.");

        var roles = await _userManager.GetRolesAsync(user);
        var vm = _mapper.Map<UserViewModel>(user);
        vm.Roles = [.. roles];
        return vm;
    }

    public async Task<UserViewModel> CreateAsync(UserViewModel model, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(model.Password))
            throw new CustomException(HttpStatusCode.BadRequest, "Mot de passe requis.");

        var existing = await _userManager.FindByEmailAsync(model.Email);
        if (existing is not null)
            throw new CustomException(HttpStatusCode.Conflict, "Email déjà utilisé.");

        var user = _mapper.Map<User>(model);
        user.Id = ChoraleDbContext.NewIdGuid().ToString();
        user.UserName = model.Email;
        user.IsActive = true;

        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
            throw new CustomException(HttpStatusCode.BadRequest, "Création échouée.",
                result.Errors.Select(e => e.Description).ToList());

        await _userManager.AddToRoleAsync(user, UserRoleEnum.Singer.ToString());

        _auditLogService.Record("SingerCreated", nameof(User), user.Id, $"Email={user.Email}");
        await _context.SaveChangesAsync(ct);

        var vm = _mapper.Map<UserViewModel>(user);
        vm.Roles = [UserRoleEnum.Singer.ToString()];
        return vm;
    }

    public async Task<UserViewModel> UpdateAsync(UserViewModel model, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(model.Id))
            throw new CustomException(HttpStatusCode.BadRequest, "Id requis.");

        var user = await LoadSingerAsync(model.Id, ct);

        user.Firstname = model.Firstname;
        user.Lastname = model.Lastname;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
            throw new CustomException(HttpStatusCode.BadRequest, "Mise à jour échouée.",
                result.Errors.Select(e => e.Description).ToList());

        _auditLogService.Record("SingerIdentityUpdated", nameof(User), user.Id);
        await _context.SaveChangesAsync(ct);

        return _mapper.Map<UserViewModel>(user);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new CustomException(HttpStatusCode.BadRequest, "Id requis.");

        var user = await LoadSingerAsync(id, ct);

        user.IsDeleted = true;
        user.IsActive = false;
        await _userManager.UpdateAsync(user);

        _auditLogService.Record("SingerDeleted", nameof(User), user.Id);
        await _context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Point unique pour Update/Delete : charge un compte TRACKE (les deux le mutent), le
    /// restreint au role Singer (meme regle que GetByIdAsync) et exclut les comptes deja
    /// IsDeleted — <c>FindByIdAsync</c> ne filtre pas IsDeleted nativement, contrairement a
    /// la requete AsNoTracking de GetByIdAsync. Choix delibere pour que ce point commun soit
    /// cohérent quelle que soit la methode appelante : aucun flux de restauration n'existe
    /// aujourd'hui, donc un compte deja supprime n'a pas de raison d'etre lisible/mutable ici.
    /// </summary>
    private async Task<User> LoadSingerAsync(string id, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(id)
            ?? throw new KeyNotFoundException($"Singer {id} not found.");

        if (user.IsDeleted || !await _userManager.IsInRoleAsync(user, UserRoleEnum.Singer.ToString()))
            throw new KeyNotFoundException($"Singer {id} not found.");

        return user;
    }
}
