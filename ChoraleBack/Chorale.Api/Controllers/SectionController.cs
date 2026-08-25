using ChoraleBackEnd.Api.Authorization;
using ChoraleBackEnd.Services.ChoirServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ChoraleBackEnd.ViewModels.Choirs;

namespace ChoraleBackEnd.Api.Controllers;

[ApiController]
[Authorize(AuthorizationPolicies.Bearer)]
[Route("api/sections")]
public sealed class SectionController(ISectionService sectionService) : ControllerBase
{
    [HttpGet("GetById")]
    public async Task<ActionResult<SectionViewModel>> GetById(
        Guid id, CancellationToken cancellationToken = default)
        => Ok(await sectionService.GetByIdAsync(id, cancellationToken));

    [HttpPut("UpdateLeader")]
    [Authorize(AuthorizationPolicies.ChoirManagerOrSectionLeader)]
    public async Task<ActionResult<SectionViewModel>> UpdateLeader(
        Guid id, [FromBody] UpdateSectionLeaderViewModel request,
        CancellationToken cancellationToken = default)
        => Ok(await sectionService.UpdateLeaderAsync(id, request.SectionLeaderId, cancellationToken));

    [HttpPost("{sectionId:guid}/AddMember")]
    [Authorize(AuthorizationPolicies.ChoirManagerOrSectionLeader)]
    public async Task<IActionResult> AddMember(
        Guid sectionId, [FromBody] AddMemberViewModel request,
        CancellationToken cancellationToken = default)
    {
        await sectionService.AddMemberAsync(sectionId, request.UserId, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{sectionId:guid}/RemoveMember/{userId}")]
    [Authorize(AuthorizationPolicies.ChoirManagerOrSectionLeader)]
    public async Task<IActionResult> RemoveMember(
        Guid sectionId, string userId, CancellationToken cancellationToken = default)
    {
        await sectionService.RemoveMemberAsync(sectionId, userId, cancellationToken);
        return NoContent();
    }
}
