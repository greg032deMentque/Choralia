using ChoraleBackEnd.Api.Authorization;
using ChoraleBackEnd.Services.UserServices;
using ChoraleBackEnd.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ChoraleBackEnd.ViewModels.Users;

namespace ChoraleBackEnd.Api.Controllers;

[ApiController]
[Authorize(AuthorizationPolicies.Bearer)]
[Route("api/singers")]
public sealed class SingerController(ISingerService singerService) : ControllerBase
{
    [HttpPost("GetPaged")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PagedListViewModel<UserViewModel>>> GetPaged(
        [FromQuery] PaginateViewModel pagination, CancellationToken cancellationToken = default)
        => Ok(await singerService.GetPagedAsync(pagination, cancellationToken));

    [HttpGet("GetById")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<UserViewModel>> GetById(
        string id, CancellationToken cancellationToken = default)
        => Ok(await singerService.GetByIdAsync(id, cancellationToken));

    [HttpPost("Create")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<UserViewModel>> Create(
        [FromBody] UserViewModel request, CancellationToken cancellationToken = default)
    {
        var created = await singerService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("Update")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<UserViewModel>> Update(
        string id, [FromBody] UserViewModel request, CancellationToken cancellationToken = default)
    {
        request.Id = id;
        return Ok(await singerService.UpdateAsync(request, cancellationToken));
    }

    [HttpDelete("Delete")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(
        string id, CancellationToken cancellationToken = default)
    {
        await singerService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
