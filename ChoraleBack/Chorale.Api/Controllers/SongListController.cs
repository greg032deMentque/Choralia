using ChoraleBackEnd.Api.Authorization;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.SongLists;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChoraleBackEnd.Api.Controllers;

[ApiController]
[Authorize(AuthorizationPolicies.Bearer)]
[Route("api/song-lists")]
public sealed class SongListController(ISongListService songListService) : ControllerBase
{
    [HttpPost("GetPaged")]
    public async Task<ActionResult<PagedListViewModel<SongListViewModel>>> GetPaged(
        [FromQuery] SongListPagedFilterViewModel request, CancellationToken cancellationToken = default)
        => Ok(await songListService.GetPagedAsync(request, cancellationToken));

    [HttpGet("GetById")]
    public async Task<ActionResult<SongListViewModel>> GetById(
        Guid id, CancellationToken cancellationToken = default)
        => Ok(await songListService.GetByIdAsync(id, cancellationToken));

    [HttpPost("Create")]
    [Authorize(AuthorizationPolicies.ChoirManagerOrSectionLeader)]
    public async Task<ActionResult<SongListViewModel>> Create(
        [FromBody] SongListViewModel request, CancellationToken cancellationToken = default)
    {
        var created = await songListService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("Update")]
    [Authorize(AuthorizationPolicies.ChoirManagerOrSectionLeader)]
    public async Task<ActionResult<SongListViewModel>> Update(
        Guid id, [FromBody] SongListViewModel request, CancellationToken cancellationToken = default)
    {
        request.Id = id;
        return Ok(await songListService.UpdateAsync(request, cancellationToken));
    }

    [HttpDelete("Delete")]
    [Authorize(AuthorizationPolicies.ChoirManagerOrSectionLeader)]
    public async Task<IActionResult> Delete(
        Guid id, CancellationToken cancellationToken = default)
    {
        await songListService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{songListId:guid}/AddSong")]
    public async Task<ActionResult<SongListViewModel>> AddSong(
        Guid songListId, [FromBody] AddSongViewModel request,
        CancellationToken cancellationToken = default)
        => Ok(await songListService.AddSongAsync(songListId, request, cancellationToken));

    [HttpDelete("{songListId:guid}/RemoveSong/{songId:guid}")]
    public async Task<IActionResult> RemoveSong(
        Guid songListId, Guid songId, CancellationToken cancellationToken = default)
    {
        await songListService.RemoveSongAsync(songListId, songId, cancellationToken);
        return NoContent();
    }

    [HttpPost("{songListId:guid}/ReorderSongs")]
    public async Task<ActionResult<SongListViewModel>> ReorderSongs(
        Guid songListId, [FromBody] ReorderSongsViewModel request,
        CancellationToken cancellationToken = default)
        => Ok(await songListService.ReorderSongsAsync(songListId, request, cancellationToken));

    [HttpPost("{id:guid}/Publish")]
    [Authorize(AuthorizationPolicies.ChoirManagerOrSectionLeader)]
    public async Task<ActionResult<SongListViewModel>> Publish(
        Guid id, CancellationToken cancellationToken = default)
        => Ok(await songListService.PublishAsync(id, cancellationToken));

    [HttpPost("{id:guid}/Archive")]
    [Authorize(AuthorizationPolicies.ChoirManagerOrSectionLeader)]
    public async Task<ActionResult<SongListViewModel>> Archive(
        Guid id, CancellationToken cancellationToken = default)
        => Ok(await songListService.ArchiveAsync(id, cancellationToken));

    [HttpPost("{id:guid}/RevertToDraft")]
    [Authorize(AuthorizationPolicies.ChoirManagerOrSectionLeader)]
    public async Task<ActionResult<SongListViewModel>> RevertToDraft(
        Guid id, CancellationToken cancellationToken = default)
        => Ok(await songListService.RevertToDraftAsync(id, cancellationToken));
}
