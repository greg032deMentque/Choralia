using ChoraleBackEnd.Api.Authorization;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.Scores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChoraleBackEnd.Api.Controllers;

[ApiController]
[Authorize(AuthorizationPolicies.Bearer)]
[Route("api/scores")]
public sealed class ScoreController(IScoreService scoreService) : ControllerBase
{
    private const long MaxFileSize = 20 * 1024 * 1024;

    [HttpPost("GetPaged")]
    public async Task<ActionResult<PagedListViewModel<ScoreViewModel>>> GetPaged(
        [FromQuery] ScorePagedFilterViewModel request, CancellationToken cancellationToken = default)
        => Ok(await scoreService.GetPagedAsync(request, cancellationToken));

    [HttpPost("GetPagedBySong")]
    public async Task<ActionResult<PagedListViewModel<ScoreViewModel>>> GetPagedBySong(
        [FromQuery] ScoreBySongFilterViewModel request, CancellationToken cancellationToken = default)
        => Ok(await scoreService.GetPagedBySongAsync(request, cancellationToken));

    [HttpGet("GetById")]
    public async Task<ActionResult<ScoreViewModel>> GetById(
        Guid id, CancellationToken cancellationToken = default)
        => Ok(await scoreService.GetByIdAsync(id, cancellationToken));

    [HttpPost("Create")]
    [Authorize(AuthorizationPolicies.ChoirManagerOrSectionLeader)]
    [RequestSizeLimit(MaxFileSize)]
    public async Task<ActionResult<ScoreViewModel>> Create(
        [FromForm] CreateScoreViewModel request, CancellationToken cancellationToken = default)
    {
        var created = await scoreService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("Update")]
    [Authorize(AuthorizationPolicies.ChoirManagerOrSectionLeader)]
    public async Task<ActionResult<ScoreViewModel>> Update(
        Guid id, [FromBody] UpdateScoreViewModel request, CancellationToken cancellationToken = default)
        => Ok(await scoreService.UpdateAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/Publish")]
    [Authorize(AuthorizationPolicies.ChoirManagerOrSectionLeader)]
    public async Task<ActionResult<ScoreViewModel>> Publish(
        Guid id, CancellationToken cancellationToken = default)
        => Ok(await scoreService.PublishAsync(id, cancellationToken));

    [HttpPost("{id:guid}/Archive")]
    [Authorize(AuthorizationPolicies.ChoirManagerOrSectionLeader)]
    public async Task<ActionResult<ScoreViewModel>> Archive(
        Guid id, CancellationToken cancellationToken = default)
        => Ok(await scoreService.ArchiveAsync(id, cancellationToken));

    [HttpPost("{id:guid}/Restore")]
    [Authorize(AuthorizationPolicies.ChoirManagerOrSectionLeader)]
    public async Task<ActionResult<ScoreViewModel>> Restore(
        Guid id, CancellationToken cancellationToken = default)
        => Ok(await scoreService.RestoreAsync(id, cancellationToken));

    [HttpDelete("Delete")]
    [Authorize(AuthorizationPolicies.ChoirManagerOrSectionLeader)]
    public async Task<IActionResult> Delete(
        Guid id, CancellationToken cancellationToken = default)
    {
        await scoreService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/Stream")]
    public async Task<IActionResult> Stream(
        Guid id, CancellationToken cancellationToken = default)
    {
        var (content, contentType, fileName, downloadAllowed) =
            await scoreService.StreamAsync(id, cancellationToken);

        // Passer un fileName produit Contenu-Disposition: attachment, donc un
        // telechargement. Autoriser la consultation n'autorise pas le telechargement
        // (D5) : sans nom de fichier, la reponse est servie inline.
        return downloadAllowed
            ? File(content, contentType, fileName)
            : File(content, contentType);
    }
}
