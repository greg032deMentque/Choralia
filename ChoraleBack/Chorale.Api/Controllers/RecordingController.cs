using ChoraleBackEnd.Api.Authorization;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.Recordings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChoraleBackEnd.Api.Controllers;

[ApiController]
[Authorize(AuthorizationPolicies.Bearer)]
[Route("api/recordings")]
public sealed class RecordingController(IRecordingService recordingService) : ControllerBase
{
    private const long MaxFileSize = 100 * 1024 * 1024;

    [HttpPost("GetPaged")]
    public async Task<ActionResult<PagedListViewModel<RecordingViewModel>>> GetPaged(
        [FromQuery] RecordingPagedFilterViewModel request, CancellationToken cancellationToken = default)
        => Ok(await recordingService.GetPagedAsync(request, cancellationToken));

    [HttpPost("GetPagedBySong")]
    public async Task<ActionResult<PagedListViewModel<RecordingViewModel>>> GetPagedBySong(
        [FromQuery] RecordingBySongFilterViewModel request, CancellationToken cancellationToken = default)
        => Ok(await recordingService.GetPagedBySongAsync(request, cancellationToken));

    [HttpGet("GetById")]
    public async Task<ActionResult<RecordingViewModel>> GetById(
        Guid id, CancellationToken cancellationToken = default)
        => Ok(await recordingService.GetByIdAsync(id, cancellationToken));

    [HttpPost("Create")]
    [Authorize(AuthorizationPolicies.ChoirManagerOrSectionLeader)]
    [RequestSizeLimit(MaxFileSize)]
    public async Task<ActionResult<RecordingViewModel>> Create(
        [FromForm] CreateRecordingViewModel request, CancellationToken cancellationToken = default)
    {
        var created = await recordingService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("Update")]
    [Authorize(AuthorizationPolicies.ChoirManagerOrSectionLeader)]
    public async Task<ActionResult<RecordingViewModel>> Update(
        Guid id, [FromBody] UpdateRecordingViewModel request, CancellationToken cancellationToken = default)
        => Ok(await recordingService.UpdateAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/SubmitForReview")]
    [Authorize(AuthorizationPolicies.ChoirManagerOrSectionLeader)]
    public async Task<ActionResult<RecordingViewModel>> SubmitForReview(
        Guid id, CancellationToken cancellationToken = default)
        => Ok(await recordingService.SubmitForReviewAsync(id, cancellationToken));

    [HttpPost("{id:guid}/Publish")]
    [Authorize(AuthorizationPolicies.ChoirManager)]
    public async Task<ActionResult<RecordingViewModel>> Publish(
        Guid id, CancellationToken cancellationToken = default)
        => Ok(await recordingService.PublishAsync(id, cancellationToken));

    [HttpPost("{id:guid}/Reject")]
    [Authorize(AuthorizationPolicies.ChoirManager)]
    public async Task<ActionResult<RecordingViewModel>> Reject(
        Guid id, CancellationToken cancellationToken = default)
        => Ok(await recordingService.RejectAsync(id, cancellationToken));

    [HttpPost("{id:guid}/Archive")]
    [Authorize(AuthorizationPolicies.ChoirManagerOrSectionLeader)]
    public async Task<ActionResult<RecordingViewModel>> Archive(
        Guid id, CancellationToken cancellationToken = default)
        => Ok(await recordingService.ArchiveAsync(id, cancellationToken));

    [HttpPost("{id:guid}/Restore")]
    [Authorize(AuthorizationPolicies.ChoirManagerOrSectionLeader)]
    public async Task<ActionResult<RecordingViewModel>> Restore(
        Guid id, CancellationToken cancellationToken = default)
        => Ok(await recordingService.RestoreAsync(id, cancellationToken));

    [HttpDelete("Delete")]
    [Authorize(AuthorizationPolicies.ChoirManagerOrSectionLeader)]
    public async Task<IActionResult> Delete(
        Guid id, CancellationToken cancellationToken = default)
    {
        await recordingService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/Stream")]
    public async Task<IActionResult> Stream(
        Guid id, CancellationToken cancellationToken = default)
    {
        var (content, contentType, fileName, downloadAllowed) =
            await recordingService.StreamAsync(id, cancellationToken);

        // Pas de enableRangeProcessing : le lecteur du front recupere la piste entiere en un
        // seul GET (`recording.service.ts`, `responseType: 'blob'`) et n'emet jamais de
        // requete Range. L'activer annoncerait un Accept-Ranges que rien ne consomme.
        // Comme pour les partitions, l'ecoute n'implique pas le telechargement (D5) : sans
        // nom de fichier, la reponse est servie inline.
        return downloadAllowed
            ? File(content, contentType, fileName)
            : File(content, contentType);
    }

    [HttpGet("EventPlaylistByVoicePart")]
    public async Task<ActionResult<List<PlaylistTrackViewModel>>> EventPlaylistByVoicePart(
        Guid eventId, VoicePartEnum voicePart, CancellationToken cancellationToken = default)
        => Ok(await recordingService.GetEventPlaylistByVoicePartAsync(eventId, voicePart, cancellationToken));
}
