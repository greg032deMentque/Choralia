using System.Net;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.Instructions;
using Microsoft.EntityFrameworkCore;

namespace ChoraleBackEnd.Services.ChoirServices;

public interface IInstructionService
{
    Task<PagedListViewModel<InstructionViewModel>> GetPagedAsync(InstructionPagedFilterViewModel filter, CancellationToken ct = default);
    Task<InstructionViewModel> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<InstructionViewModel> CreateAsync(CreateInstructionViewModel model, CancellationToken ct = default);
    Task<InstructionViewModel> UpdateAsync(UpdateInstructionViewModel model, CancellationToken ct = default);
    Task<InstructionViewModel> PublishAsync(Guid id, CancellationToken ct = default);
    Task<InstructionViewModel> ArchiveAsync(Guid id, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// Consignes de chant. Cible unique depuis la migration `InstructionsSongScopeOnly` : la
/// chorale d'une consigne se deduit de <c>Song.ChoirId</c>, jamais d'une colonne propre.
/// </summary>
public sealed class InstructionService : BaseService, IInstructionService
{
    private readonly IMembershipService _membershipService;

    public InstructionService(IServiceProvider serviceProvider, IMembershipService membershipService)
        : base(serviceProvider)
    {
        _membershipService = membershipService;
    }

    public async Task<PagedListViewModel<InstructionViewModel>> GetPagedAsync(
        InstructionPagedFilterViewModel filter, CancellationToken ct = default)
    {
        var query = _context.Instructions
            .AsNoTracking()
            .Include(c => c.Author)
            .AsQueryable();

        if (filter.SongId.HasValue) query = query.Where(c => c.SongId == filter.SongId.Value);
        if (filter.VoicePart.HasValue) query = query.Where(c => c.VoicePart == filter.VoicePart.Value);
        if (filter.Status.HasValue) query = query.Where(c => c.Status == filter.Status.Value);

        if (!string.IsNullOrWhiteSpace(filter.Filter))
            query = query.Where(c => c.Content.Contains(filter.Filter)
                                     || c.Title != null && c.Title.Contains(filter.Filter));

        query = await RestrictVisibilityAsync(query, ct);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Skip(filter.Offset)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return new PagedListViewModel<InstructionViewModel>
        {
            Items = _mapper.Map<List<InstructionViewModel>>(items),
            TotalCount = total,
            CurrentPage = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<InstructionViewModel> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var instruction = await LoadAsync(id, ct);
        await EnsureReadAsync(instruction, ct);
        return _mapper.Map<InstructionViewModel>(instruction);
    }

    public async Task<InstructionViewModel> CreateAsync(
        CreateInstructionViewModel model, CancellationToken ct = default)
    {
        var choirId = await ResolveChoirAsync(model.SongId, ct);
        await EnsureWriteAsync(choirId, model.VoicePart, ct);

        var userId = _currentUserId
            ?? throw new CustomException(HttpStatusCode.Unauthorized, "Utilisateur non authentifié.");

        var instruction = new Instruction
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SongId = model.SongId,
            VoicePart = model.VoicePart,
            Title = model.Title,
            Content = model.Content,
            Status = InstructionStatusEnum.Draft,
            AuthorUserId = userId,
            IsDeleted = false
        };

        _context.Instructions.Add(instruction);
        await _context.SaveChangesAsync(ct);
        return await GetByIdAsync(instruction.Id, ct);
    }

    public async Task<InstructionViewModel> UpdateAsync(
        UpdateInstructionViewModel model, CancellationToken ct = default)
    {
        var instruction = await LoadAsync(model.Id, ct);
        await EnsureWriteOnAsync(instruction, ct);

        if (instruction.Status == InstructionStatusEnum.Archived)
            throw new CustomException(HttpStatusCode.Conflict,
                "Une consigne archivée ne peut plus être modifiée.");

        instruction.Title = model.Title;
        instruction.Content = model.Content;
        await _context.SaveChangesAsync(ct);
        return await GetByIdAsync(instruction.Id, ct);
    }

    public async Task<InstructionViewModel> PublishAsync(Guid id, CancellationToken ct = default)
    {
        var instruction = await LoadAsync(id, ct);
        await EnsureWriteOnAsync(instruction, ct);

        if (instruction.Status != InstructionStatusEnum.Draft)
            throw new CustomException(HttpStatusCode.Conflict,
                "Seule une consigne en brouillon peut être publiée.");

        instruction.Status = InstructionStatusEnum.Published;
        instruction.PublishedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return await GetByIdAsync(instruction.Id, ct);
    }

    public async Task<InstructionViewModel> ArchiveAsync(Guid id, CancellationToken ct = default)
    {
        var instruction = await LoadAsync(id, ct);
        await EnsureWriteOnAsync(instruction, ct);

        instruction.Status = InstructionStatusEnum.Archived;
        await _context.SaveChangesAsync(ct);
        return await GetByIdAsync(instruction.Id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var instruction = await LoadAsync(id, ct);
        await EnsureWriteOnAsync(instruction, ct);

        instruction.IsDeleted = true;
        await _context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Chorale porteuse de la consigne : celle du chant. Un chant introuvable est une erreur
    /// d'appelant, pas une absence de scope — sortir en autorisant rendrait la consigne
    /// ecrivable par n'importe quel compte authentifie.
    /// </summary>
    private async Task<Guid> ResolveChoirAsync(Guid songId, CancellationToken ct)
        => await _context.Songs
               .Where(c => c.Id == songId)
               .Select(c => (Guid?)c.ChoirId)
               .FirstOrDefaultAsync(ct)
           ?? throw new KeyNotFoundException($"Song {songId} not found.");

    private async Task EnsureWriteOnAsync(Instruction instruction, CancellationToken ct)
        => await EnsureWriteAsync(await ResolveChoirAsync(instruction.SongId, ct), instruction.VoicePart, ct);

    /// <summary>
    /// Droit d'ecriture : responsable de la chorale du chant, ou chef de pupitre pour sa seule
    /// voix (`02` § Matrice, domaine Consigne).
    /// </summary>
    private async Task EnsureWriteAsync(Guid choirId, VoicePartEnum? voicePart, CancellationToken ct)
    {
        // Point de passage unique pour Create/Update/Publish/Archive/Delete : plus aucune
        // ecriture des que la chorale n'est plus Publie/Draft, y compris pour l'Admin.
        await _membershipService.EnsureCanWriteAsync(choirId, ct);

        if (_currentUserRoles.Contains(UserRoleEnum.Admin)) return;

        var isManager = await _context.SpaceMemberRoles
            .AnyAsync(r => r.Role == UserRoleEnum.Manager
                           && r.SpaceMember.UserId == _currentUserId
                           && r.SpaceMember.ChoirId == choirId, ct);

        if (isManager) return;

        // Le chef de pupitre n'ecrit que sur SA voix. La voix est donc obligatoire pour lui :
        // une consigne de chant sans voix s'adresse a tout le choeur et releve du responsable.
        if (voicePart is { } targetVoicePart)
        {
            var isLeaderOfThisVoicePart = await _context.Sections
                .AnyAsync(p => p.ChoirId == choirId
                               && p.SectionLeaderId == _currentUserId
                               && p.VoicePart == targetVoicePart, ct);

            if (isLeaderOfThisVoicePart) return;
        }

        throw new CustomException(HttpStatusCode.Forbidden, "Accès refusé à cette consigne.");
    }

    private async Task EnsureReadAsync(Instruction instruction, CancellationToken ct)
    {
        if (_currentUserRoles.Contains(UserRoleEnum.Admin)) return;

        var choirId = await ResolveChoirAsync(instruction.SongId, ct);
        await _membershipService.EnsureMemberActiveAsync(choirId, ct);

        // Un brouillon n'est visible que de son author et des responsables — son existence
        // meme ne doit pas etre revelee aux membres (`02` § Regles de visibilite).
        if (instruction.Status == InstructionStatusEnum.Draft
            && instruction.AuthorUserId != _currentUserId)
        {
            var isManager = await _context.SpaceMemberRoles
                .AnyAsync(r => r.Role == UserRoleEnum.Manager
                               && r.SpaceMember.UserId == _currentUserId
                               && r.SpaceMember.ChoirId == choirId, ct);

            if (!isManager)
                throw new KeyNotFoundException($"Instruction {instruction.Id} not found.");
        }
    }

    private async Task<IQueryable<Instruction>> RestrictVisibilityAsync(
        IQueryable<Instruction> query, CancellationToken ct)
    {
        if (_currentUserRoles.Contains(UserRoleEnum.Admin)) return query;

        // Passe par la source unique : statut actif et client actif compris. Une requete qui ne
        // testerait que la presence d'une ligne d'appartenance laisserait un membre desactive
        // lire les consignes de sa chorale.
        var mesChoirIds = await _membershipService.ChoirsAccessibleAsync(ct);

        // Chorales ou je suis responsable, pre-calculees : la regle de visibilite des brouillons
        // ci-dessous en a besoin par ligne, et un sous-select par consigne serait ecrit une fois
        // par ligne du resultat.
        var managedChoirIds = await _context.SpaceMemberRoles
            .Where(r => r.Role == UserRoleEnum.Manager && r.SpaceMember.UserId == _currentUserId)
            .Select(r => r.SpaceMember.ChoirId)
            .Where(id => id != null)
            .Select(id => id!.Value)
            .Distinct()
            .ToListAsync(ct);

        // Restriction d'appartenance appliquee inconditionnellement, puis la regle de statut.
        // Le brouillon est visible de son auteur ET des responsables de la chorale : la liste
        // s'alignait auparavant sur le seul auteur, alors qu'EnsureReadAsync autorisait deja le
        // responsable — un responsable ne voyait donc jamais dans la liste les brouillons que
        // GetById lui servait pourtant (`02` § Regles de visibilite).
        return query
            .Where(c => _context.Songs.Any(ch => ch.Id == c.SongId && mesChoirIds.Contains(ch.ChoirId)))
            .Where(c => c.Status != InstructionStatusEnum.Draft
                        || c.AuthorUserId == _currentUserId
                        || _context.Songs.Any(ch => ch.Id == c.SongId && managedChoirIds.Contains(ch.ChoirId)));
    }

    private async Task<Instruction> LoadAsync(Guid id, CancellationToken ct)
        => await _context.Instructions
               .Include(c => c.Author)
               .FirstOrDefaultAsync(c => c.Id == id, ct)
           ?? throw new KeyNotFoundException($"Instruction {id} not found.");
}
