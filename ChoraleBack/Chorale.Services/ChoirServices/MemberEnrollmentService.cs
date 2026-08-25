using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChoraleBackEnd.Services.ChoirServices;

/// <summary>
/// Point unique de l'entrée d'une personne dans une chorale : ligne d'appartenance
/// (<c>SpaceMember</c>), voix (<c>SectionMember</c>) et rôle d'espace
/// (<c>SpaceMemberRole</c>).
/// </summary>
/// <remarks>
/// La séquence était écrite deux fois — invitation nominative
/// (<c>ChoirMembersService.InviteAsync</c>) et admission d'une demande
/// (<c>MembershipRequestService.ApproveAsync</c>) — et les deux copies avaient déjà divergé :
/// l'invitation ne créait aucune voix, alors que `02` §132 impose qu'une ligne
/// <c>SpaceMember</c> en porte toujours une.
/// </remarks>
public interface IMemberEnrollmentService
{
    /// <summary>
    /// Ajoute au contexte l'appartenance et ses lignes liées, SANS committer : l'appelant
    /// enregistre, ce qui lui laisse la possibilité de committer dans la même transaction
    /// que ses propres mutations (traitement de la demande d'adhésion, par exemple).
    /// </summary>
    /// <param name="primaryVoicePart">
    /// Voix principale. Absente, aucune <c>SectionMember</c> n'est créée — cas de
    /// l'invitation émise avant que le front ne propose le champ.
    /// </param>
    /// <param name="role">
    /// Rôle d'espace supplémentaire. <c>Singer</c> est implicite et ne produit aucune ligne :
    /// seul <c>Manager</c> est matérialisé.
    /// </param>
    /// <exception cref="KeyNotFoundException">
    /// Voix fournie sans section correspondante dans la chorale.
    /// </exception>
    Task<SpaceMember> EnrollAsync(
        Guid choirId,
        string userId,
        MemberStatusEnum status,
        VoicePartEnum? primaryVoicePart,
        UserRoleEnum? role,
        CancellationToken ct = default);
}

public sealed class MemberEnrollmentService : BaseService, IMemberEnrollmentService
{
    public MemberEnrollmentService(IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
    }

    public async Task<SpaceMember> EnrollAsync(
        Guid choirId,
        string userId,
        MemberStatusEnum status,
        VoicePartEnum? primaryVoicePart,
        UserRoleEnum? role,
        CancellationToken ct = default)
    {
        var member = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = userId,
            ChoirId = choirId,
            SpaceId = choirId,
            Status = status,
            IsDeleted = false
        };
        _context.SpaceMembers.Add(member);

        if (primaryVoicePart is { } voicePart)
        {
            var sectionId = await _context.Sections
                .AsNoTracking()
                .Where(s => s.ChoirId == choirId && s.VoicePart == voicePart)
                .Select(s => (Guid?)s.Id)
                .FirstOrDefaultAsync(ct)
                ?? throw new KeyNotFoundException("Section not found for this voice part.");

            _context.SectionMembers.Add(new SectionMember
            {
                Id = ChoraleDbContext.NewIdGuid(),
                UserId = userId,
                SectionId = sectionId
            });
        }

        if (role == UserRoleEnum.Manager)
        {
            _context.SpaceMemberRoles.Add(new SpaceMemberRole
            {
                Id = ChoraleDbContext.NewIdGuid(),
                SpaceMemberId = member.Id,
                Role = UserRoleEnum.Manager
            });
        }

        return member;
    }
}
