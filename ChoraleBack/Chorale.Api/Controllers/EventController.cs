using ChoraleBackEnd.Api.Authorization;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ChoraleBackEnd.ViewModels.Events;

namespace ChoraleBackEnd.Api.Controllers;

[ApiController]
[Authorize(AuthorizationPolicies.Bearer)]
[Route("api/events")]
public sealed class EventController(IEventService eventService) : ControllerBase
{
    [HttpPost("GetPaged")]
    public async Task<ActionResult<PagedListViewModel<EventViewModel>>> GetPaged(
        [FromQuery] EventPagedFilterViewModel request, CancellationToken cancellationToken = default)
        => Ok(await eventService.GetPagedAsync(request, cancellationToken));

    [HttpGet("GetById")]
    public async Task<ActionResult<EventViewModel>> GetById(
        Guid id, CancellationToken cancellationToken = default)
        => Ok(await eventService.GetByIdAsync(id, cancellationToken));

    [HttpPost("Create")]
    public async Task<ActionResult<EventViewModel>> Create(
        [FromBody] EventViewModel request, CancellationToken cancellationToken = default)
    {
        var created = await eventService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("Update")]
    [Authorize(AuthorizationPolicies.SpaceManager)]
    public async Task<ActionResult<EventViewModel>> Update(
        Guid id, [FromBody] EventViewModel request, CancellationToken cancellationToken = default)
    {
        request.Id = id;
        return Ok(await eventService.UpdateAsync(request, cancellationToken));
    }

    [HttpDelete("Delete")]
    [Authorize(AuthorizationPolicies.SpaceManager)]
    public async Task<IActionResult> Delete(
        Guid id, CancellationToken cancellationToken = default)
    {
        await eventService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("ChangeStatus")]
    [Authorize(AuthorizationPolicies.SpaceManager)]
    public async Task<ActionResult<EventViewModel>> ChangeStatus(
        Guid id,
        [FromQuery] Common.Enums.EventStatusEnum status,
        CancellationToken cancellationToken = default)
        => Ok(await eventService.ChangeStatusAsync(id, status, cancellationToken));

    [HttpPost("Close")]
    [Authorize(AuthorizationPolicies.SpaceManager)]
    public async Task<ActionResult<EventViewModel>> Close(
        Guid id, CancellationToken cancellationToken = default)
        => Ok(await eventService.CloseAsync(id, cancellationToken));
}
