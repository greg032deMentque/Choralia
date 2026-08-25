using ChoraleBackEnd.Api.Authorization;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.ChoirMembers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChoraleBackEnd.Api.Controllers;

[ApiController]
[Authorize(AuthorizationPolicies.Bearer)]
[Authorize(AuthorizationPolicies.ChoirManager)]
[Route("api/choir-members")]
public sealed class ChoirMembersController(
    IChoirMembersService choirMembersService,
    ISpaceContextAccessor spaceContextAccessor) : ControllerBase
{
    [HttpPost("GetPaged")]
    public async Task<ActionResult<PagedListViewModel<ChoirMemberListItemViewModel>>> GetPaged(
        [FromQuery] PaginateViewModel pagination, CancellationToken cancellationToken = default)
        => Ok(await choirMembersService.GetPagedAsync(
            spaceContextAccessor.RequireSpaceId(), pagination, cancellationToken));

    [HttpGet("GetById")]
    public async Task<ActionResult<ChoirMemberListItemViewModel>> GetById(
        Guid id, CancellationToken cancellationToken = default)
        => Ok(await choirMembersService.GetByIdAsync(
            spaceContextAccessor.RequireSpaceId(), id, cancellationToken));

    [HttpPost("Invite")]
    public async Task<ActionResult<ChoirMemberListItemViewModel>> Invite(
        [FromBody] InviteMemberViewModel request, CancellationToken cancellationToken = default)
    {
        var created = await choirMembersService.InviteAsync(
            spaceContextAccessor.RequireSpaceId(), request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("Update")]
    public async Task<ActionResult<ChoirMemberListItemViewModel>> Update(
        [FromBody] UpdateChoirMemberViewModel request, CancellationToken cancellationToken = default)
        => Ok(await choirMembersService.UpdateAsync(
            spaceContextAccessor.RequireSpaceId(), request, cancellationToken));

    [HttpPut("ChangeRole")]
    public async Task<ActionResult<ChoirMemberListItemViewModel>> ChangeRole(
        [FromBody] ChangeMemberRoleViewModel request, CancellationToken cancellationToken = default)
        => Ok(await choirMembersService.ChangeRoleAsync(
            spaceContextAccessor.RequireSpaceId(), request, cancellationToken));

    [HttpPut("ChangeStatus")]
    public async Task<ActionResult<ChoirMemberListItemViewModel>> ChangeStatus(
        [FromBody] ChangeMemberStatusViewModel request, CancellationToken cancellationToken = default)
        => Ok(await choirMembersService.ChangeStatusAsync(
            spaceContextAccessor.RequireSpaceId(), request, cancellationToken));
}
