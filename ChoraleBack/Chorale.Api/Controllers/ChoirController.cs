using ChoraleBackEnd.Api.Authorization;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ChoraleBackEnd.ViewModels.Choirs;

namespace ChoraleBackEnd.Api.Controllers;

[ApiController]
[Authorize(AuthorizationPolicies.Bearer)]
[Route("api/choirs")]
public sealed class ChoirController(IChoirService choirService) : ControllerBase
{
    [HttpPost("GetPaged")]
    public async Task<ActionResult<PagedListViewModel<ChoirViewModel>>> GetPaged(
        [FromQuery] PaginateViewModel pagination, CancellationToken cancellationToken = default)
        => Ok(await choirService.GetPagedAsync(pagination, cancellationToken));

    [HttpGet("GetById")]
    public async Task<ActionResult<ChoirViewModel>> GetById(
        Guid id, CancellationToken cancellationToken = default)
        => Ok(await choirService.GetByIdAsync(id, cancellationToken));

    [HttpPost("Create")]
    [Authorize(AuthorizationPolicies.AdminOrClientManager)]
    public async Task<ActionResult<ChoirViewModel>> Create(
        [FromBody] ChoirViewModel request, CancellationToken cancellationToken = default)
    {
        var created = await choirService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("Update")]
    [Authorize(AuthorizationPolicies.AdminOrClientManager)]
    public async Task<ActionResult<ChoirViewModel>> Update(
        Guid id, [FromBody] ChoirViewModel request, CancellationToken cancellationToken = default)
    {
        request.Id = id;
        return Ok(await choirService.UpdateAsync(request, cancellationToken));
    }

    [HttpDelete("Delete")]
    [Authorize(AuthorizationPolicies.AdminOrClientManager)]
    public async Task<IActionResult> Delete(
        Guid id, CancellationToken cancellationToken = default)
    {
        await choirService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{choirId:guid}/AddMember")]
    [Authorize(AuthorizationPolicies.SpaceManager)]
    public async Task<IActionResult> AddMember(
        Guid choirId, [FromBody] AddMemberViewModel request,
        CancellationToken cancellationToken = default)
    {
        await choirService.AddMemberAsync(choirId, request.UserId, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{choirId:guid}/RemoveMember/{userId}")]
    [Authorize(AuthorizationPolicies.SpaceManager)]
    public async Task<IActionResult> RemoveMember(
        Guid choirId, string userId, CancellationToken cancellationToken = default)
    {
        await choirService.RemoveMemberAsync(choirId, userId, cancellationToken);
        return NoContent();
    }
}
