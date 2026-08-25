using ChoraleBackEnd.Api.Authorization;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.AdminEvents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChoraleBackEnd.Api.Controllers.AdminControllers;

/// <summary>
/// Administration generale des events, transverse a tous les clients (`10-D23`). Lecture
/// seule : aucune ecriture n'est exposee ici, voir <c>EventController</c>.
/// </summary>
[ApiController]
[Authorize(AuthorizationPolicies.Bearer)]
[Authorize(Roles = "Admin")]
[Route("api/admin-events")]
public sealed class AdminEventController(IAdminEventService adminEventService) : ControllerBase
{
    [HttpPost("GetPaged")]
    public async Task<ActionResult<PagedListViewModel<AdminEventListItemViewModel>>> GetPaged(
        [FromQuery] AdminEventsPagedFilterViewModel request, CancellationToken cancellationToken = default)
        => Ok(await adminEventService.GetPagedAsync(request, cancellationToken));

    [HttpGet("{eventId:guid}")]
    public async Task<ActionResult<AdminEventDetailViewModel>> GetById(
        Guid eventId, CancellationToken cancellationToken = default)
        => Ok(await adminEventService.GetByIdAsync(eventId, cancellationToken));
}
