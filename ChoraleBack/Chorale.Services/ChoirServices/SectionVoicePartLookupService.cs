using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using Microsoft.EntityFrameworkCore;

namespace ChoraleBackEnd.Services.ChoirServices;

public interface ISectionVoicePartLookupService
{
    Task<Dictionary<Guid, VoicePartEnum>> GetPrimaryVoicePartsAsync(
        string userId, IReadOnlyCollection<Guid> choirIds, CancellationToken ct = default);

    Task<Dictionary<(string UserId, Guid ChoirId), (Guid SectionId, VoicePartEnum VoicePart)>> GetPrimarySectionsAsync(
        IReadOnlyCollection<string> userIds, IReadOnlyCollection<Guid> choirIds, CancellationToken ct = default);
}

/// <remarks>
/// N'herite deliberement PAS de <c>BaseService</c>, meme justification que
/// <see cref="ChoraleBackEnd.Services.AuthServices.SpaceRoleResolverService"/> : recoit le(s)
/// <c>userId</c>/<c>userIds</c> en PARAMETRE, n'a besoin ni du contexte HTTP ni de
/// l'utilisateur courant.
/// </remarks>
public sealed class SectionVoicePartLookupService : ISectionVoicePartLookupService
{
    private readonly ChoraleDbContext _context;

    public SectionVoicePartLookupService(ChoraleDbContext context)
    {
        _context = context;
    }

    public async Task<Dictionary<Guid, VoicePartEnum>> GetPrimaryVoicePartsAsync(
        string userId, IReadOnlyCollection<Guid> choirIds, CancellationToken ct = default)
    {
        var sections = await GetPrimarySectionsAsync([userId], choirIds, ct);
        return sections.ToDictionary(kvp => kvp.Key.ChoirId, kvp => kvp.Value.VoicePart);
    }

    // Au plus une SectionMember par (UserId, ChoirId) aujourd'hui, garanti par
    // SectionService.EnsureUniqueSectionPerChoirAsync : le GroupBy/First ci-dessous
    // n'est donc pas un choix arbitraire parmi plusieurs pupitres possibles, il
    // n'y a jamais qu'un seul candidat par (utilisateur, chorale).
    public async Task<Dictionary<(string UserId, Guid ChoirId), (Guid SectionId, VoicePartEnum VoicePart)>> GetPrimarySectionsAsync(
        IReadOnlyCollection<string> userIds, IReadOnlyCollection<Guid> choirIds, CancellationToken ct = default)
    {
        if (userIds.Count == 0 || choirIds.Count == 0)
            return [];

        return (await _context.SectionMembers
                .AsNoTracking()
                .Include(sm => sm.Section)
                .Where(sm => userIds.Contains(sm.UserId) && choirIds.Contains(sm.Section.ChoirId))
                .Select(sm => new { sm.UserId, sm.Section.ChoirId, sm.SectionId, sm.Section.VoicePart })
                .ToListAsync(ct))
            .GroupBy(x => (x.UserId, x.ChoirId))
            .ToDictionary(g => g.Key, g => (g.First().SectionId, g.First().VoicePart));
    }
}
