using System.Net;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services;
using Microsoft.EntityFrameworkCore;
using ChoraleBackEnd.ViewModels.Choirs;

namespace ChoraleBackEnd.Services.ChoirServices;

public interface ISectionService
{
    Task<SectionViewModel> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<SectionViewModel> UpdateLeaderAsync(Guid id, string sectionLeaderId, CancellationToken ct = default);
    Task AddMemberAsync(Guid sectionId, string userId, CancellationToken ct = default);
    Task RemoveMemberAsync(Guid sectionId, string userId, CancellationToken ct = default);
    Task<bool> IsSectionLeaderAsync(Guid sectionId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Source unique pour « userId est-il chef d'un pupitre de cette chorale, quel qu'il
    /// soit » — remplace les 3 copies inline qui divergeaient potentiellement
    /// (<c>ChoirService.RemoveMemberAsync</c>, <c>ChoirMembersService.RevokeManagerRoleAsync</c>,
    /// <c>ChoirMembersService.ArchiveMemberAsync</c>). A la difference de
    /// <see cref="IsSectionLeaderAsync"/>, qui verifie UN pupitre precis, celle-ci porte sur
    /// la chorale entiere.
    /// </summary>
    Task<bool> IsSectionLeaderInChoirAsync(Guid choirId, string userId, CancellationToken ct = default);
}

public sealed class SectionService : BaseService, ISectionService
{
    public SectionService(IServiceProvider serviceProvider)
        : base(serviceProvider) { }

    public async Task<SectionViewModel> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var section = await _context.Sections
            .AsNoTracking()
            .Include(p => p.SectionLeader)
            .Include(p => p.Members)
                .ThenInclude(m => m.User)
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new KeyNotFoundException($"Section {id} not found.");

        await EnsureMembershipAsync(section.ChoirId, ct);

        var vm = _mapper.Map<SectionViewModel>(section);
        vm.Members = _mapper.Map<List<SectionMemberViewModel>>(section.Members);
        return vm;
    }

    public async Task<SectionViewModel> UpdateLeaderAsync(Guid id, string sectionLeaderId, CancellationToken ct = default)
    {
        var section = await _context.Sections
            .Include(p => p.Members)
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new KeyNotFoundException($"Section {id} not found.");

        // Sans ce controle, la policy HTTP ne protege rien ici : SpaceRoleAuthorizationHandler
        // resout le role sur l'espace du header X-Space-Id, jamais sur la chorale reelle de la
        // section visee — un Manager de la chorale A pouvait donc reaffecter le chef de pupitre
        // d'une chorale B (autre client) en ciblant son id de section.
        await EnsureMembershipAsync(section.ChoirId, ct);

        var isMember = section.Members.Any(m => m.UserId == sectionLeaderId);
        if (!isMember)
            throw new CustomException(
                $"Utilisateur {sectionLeaderId} doit être membre du pupitre avant d'en être chef.",
                "Le chef de pupitre doit être membre du pupitre.");

        section.SectionLeaderId = sectionLeaderId;
        await _context.SaveChangesAsync(ct);
        return _mapper.Map<SectionViewModel>(section);
    }

    public async Task AddMemberAsync(Guid sectionId, string userId, CancellationToken ct = default)
    {
        var section = await _context.Sections
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == sectionId, ct)
            ?? throw new KeyNotFoundException($"Section {sectionId} not found.");

        await EnsureMembershipAsync(section.ChoirId, ct);

        // Porte aussi le non-doublon sur CE pupitre : la garde couvre tous les pupitres de la
        // chorale, celui vise compris. Passe cette ligne, l'utilisateur n'appartient donc a
        // aucun d'eux et l'ajout est inconditionnel — un second controle « est-il deja dans
        // sectionId ? » ne pourrait jamais etre vrai.
        await EnsureUniqueSectionPerChoirAsync(section.ChoirId, userId, ct);

        _context.SectionMembers.Add(new SectionMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SectionId = sectionId,
            UserId = userId
        });

        var choirExists = await _context.SpaceMembers
            .AnyAsync(m => m.ChoirId == section.ChoirId && m.UserId == userId, ct);

        if (!choirExists)
        {
            _context.SpaceMembers.Add(new SpaceMember
            {
                Id = ChoraleDbContext.NewIdGuid(),
                ChoirId = section.ChoirId,
                SpaceId = section.ChoirId,
                UserId = userId,
                Status = MemberStatusEnum.Active,
                IsDeleted = false
            });
        }

        await _context.SaveChangesAsync(ct);
    }

    public async Task RemoveMemberAsync(Guid sectionId, string userId, CancellationToken ct = default)
    {
        var section = await _context.Sections
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == sectionId, ct)
            ?? throw new KeyNotFoundException($"Section {sectionId} not found.");

        await EnsureMembershipAsync(section.ChoirId, ct);

        var member = await _context.SectionMembers
            .FirstOrDefaultAsync(m => m.SectionId == sectionId && m.UserId == userId, ct)
            ?? throw new KeyNotFoundException("Member not found in this section.");

        _context.SectionMembers.Remove(member);

        var otherSections = await _context.SectionMembers
            .AsNoTracking()
            .Include(m => m.Section)
            .Where(m => m.UserId == userId
                && m.SectionId != sectionId
                && m.Section.ChoirId == section.ChoirId)
            .AnyAsync(ct);

        if (!otherSections)
        {
            var spaceMember = await _context.SpaceMembers
                .FirstOrDefaultAsync(m => m.ChoirId == section.ChoirId && m.UserId == userId, ct);
            if (spaceMember is not null)
                _context.SpaceMembers.Remove(spaceMember);
        }

        await _context.SaveChangesAsync(ct);
    }

    public async Task<bool> IsSectionLeaderAsync(Guid sectionId, string userId, CancellationToken ct = default)
        => await _context.Sections
            .AsNoTracking()
            .AnyAsync(p => p.Id == sectionId && p.SectionLeaderId == userId, ct);

    public async Task<bool> IsSectionLeaderInChoirAsync(Guid choirId, string userId, CancellationToken ct = default)
        => await _context.Sections
            .AsNoTracking()
            .AnyAsync(p => p.ChoirId == choirId && p.SectionLeaderId == userId, ct);

    // Appartenance ACTIVE exigee : un compte Invited (jamais reclame), Inactive ou Archived
    // n'a plus acces au pupitre. Async : l'appel EF etait synchrone alors que les 4 methodes
    // appelantes sont async, ce qui bloquait un thread du pool sur de l'I/O base.
    private async Task EnsureMembershipAsync(Guid choirId, CancellationToken ct)
    {
        var isAdmin = _currentUserRoles.Contains(UserRoleEnum.Admin);
        if (isAdmin) return;

        var isMember = await _context.SpaceMembers
            .AnyAsync(m => m.ChoirId == choirId
                && m.UserId == _currentUserId
                && m.Status == MemberStatusEnum.Active, ct);

        if (!isMember)
            throw new CustomException(
                HttpStatusCode.Forbidden,
                "Accès refusé à ce pupitre.");
    }

    private async Task EnsureUniqueSectionPerChoirAsync(
        Guid choirId, string userId, CancellationToken ct)
    {
        var alreadySectionMember = await _context.SectionMembers
            .AsNoTracking()
            .Include(m => m.Section)
            .AnyAsync(m => m.UserId == userId
                && m.Section.ChoirId == choirId, ct);

        if (alreadySectionMember)
            throw new CustomException(
                "Un chanteur ne peut appartenir qu'à un seul pupitre par chorale.",
                "Ce chanteur est déjà dans un pupitre de cette chorale.",
                HttpStatusCode.Conflict);
    }
}
