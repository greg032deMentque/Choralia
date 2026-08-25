using ChoraleBackEnd.Api.Authorization;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.ChoirMembers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChoraleBackEnd.Api.Controllers;

[ApiController]
[Authorize(AuthorizationPolicies.Bearer)]
[Authorize(AuthorizationPolicies.AdminOrClientManager)]
[Route("api/choirs/{choirId:guid}/ChoirMasters")]
public sealed class ChoirMastersController(IChoirMasterService choirMasterService) : ControllerBase
{
    [HttpPost("GetPaged")]
    public async Task<ActionResult<PagedListViewModel<ChoirMemberListItemViewModel>>> GetPaged(
        Guid choirId, [FromQuery] PaginateViewModel pagination, CancellationToken cancellationToken = default)
        => Ok(await choirMasterService.GetPagedAsync(choirId, pagination, cancellationToken));

    [HttpPut("Assign")]
    public async Task<ActionResult<ChoirMemberListItemViewModel>> Assign(
        Guid choirId, [FromBody] AssignChoirMasterViewModel request, CancellationToken cancellationToken = default)
        => Ok(await choirMasterService.AssignAsync(choirId, request, cancellationToken));

    [HttpDelete("{userId}")]
    public async Task<IActionResult> Revoke(
        Guid choirId, string userId, CancellationToken cancellationToken = default)
    {
        await choirMasterService.RevokeAsync(choirId, userId, cancellationToken);
        return NoContent();
    }
}
