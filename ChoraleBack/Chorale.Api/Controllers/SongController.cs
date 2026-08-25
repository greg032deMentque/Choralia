using ChoraleBackEnd.Api.Authorization;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ChoraleBackEnd.ViewModels.Songs;

namespace ChoraleBackEnd.Api.Controllers;

[ApiController]
[Authorize(AuthorizationPolicies.Bearer)]
[Route("api/songs")]
public sealed class SongController(ISongService songService) : ControllerBase
{
    [HttpPost("GetPaged")]
    public async Task<ActionResult<PagedListViewModel<SongViewModel>>> GetPaged(
        [FromQuery] SongPagedFilterViewModel request, CancellationToken cancellationToken = default)
        => Ok(await songService.GetPagedAsync(request, cancellationToken));

    [HttpPost("GetPagedByChoir")]
    public async Task<ActionResult<PagedListViewModel<SongViewModel>>> GetPagedByChoir(
        [FromQuery] SongByChoirFilterViewModel request, CancellationToken cancellationToken = default)
        => Ok(await songService.GetPagedByChoirAsync(request, cancellationToken));

    [HttpGet("GetById")]
    public async Task<ActionResult<SongViewModel>> GetById(
        Guid id, CancellationToken cancellationToken = default)
        => Ok(await songService.GetByIdAsync(id, cancellationToken));

    [HttpPost("Create")]
    [Authorize(AuthorizationPolicies.ChoirManagerOrSectionLeader)]
    public async Task<ActionResult<SongViewModel>> Create(
        [FromBody] SongViewModel request, CancellationToken cancellationToken = default)
    {
        var created = await songService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("Update")]
    [Authorize(AuthorizationPolicies.ChoirManagerOrSectionLeader)]
    public async Task<ActionResult<SongViewModel>> Update(
        Guid id, [FromBody] SongViewModel request, CancellationToken cancellationToken = default)
    {
        request.Id = id;
        return Ok(await songService.UpdateAsync(request, cancellationToken));
    }

    [HttpDelete("Delete")]
    [Authorize(AuthorizationPolicies.ChoirManagerOrSectionLeader)]
    public async Task<IActionResult> Delete(
        Guid id, CancellationToken cancellationToken = default)
    {
        await songService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
