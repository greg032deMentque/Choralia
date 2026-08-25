using ChoraleBackEnd.Api.Authorization;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChoraleBackEnd.Api.Controllers;

[ApiController]
[Authorize(AuthorizationPolicies.Bearer)]
[Route("api/event-participants")]
public sealed class EventParticipantController(IEventParticipantService eventParticipantService) : ControllerBase
{
    [HttpPost("Invite")]
    [Authorize(AuthorizationPolicies.SpaceManager)]
    public async Task<ActionResult<EventParticipantListItemViewModel>> Invite(
        [FromBody] InviteEventParticipantViewModel request, CancellationToken cancellationToken = default)
    {
        var created = await eventParticipantService.InviteAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpPut("Rsvp")]
    public async Task<ActionResult<EventParticipantListItemViewModel>> Rsvp(
        [FromBody] EventRsvpViewModel request, CancellationToken cancellationToken = default)
        => Ok(await eventParticipantService.RsvpAsync(request, cancellationToken));

    [HttpPost("GetPaged")]
    public async Task<ActionResult<PagedListViewModel<EventParticipantListItemViewModel>>> GetPaged(
        [FromQuery] EventParticipantsPagedFilterViewModel request, CancellationToken cancellationToken = default)
        => Ok(await eventParticipantService.GetPagedAsync(request, cancellationToken));

    [HttpDelete("Delete")]
    [Authorize(AuthorizationPolicies.SpaceManager)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        await eventParticipantService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
