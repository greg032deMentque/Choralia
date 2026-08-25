using ChoraleBackEnd.Api.Authorization;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.AdminChoirs;
using ChoraleBackEnd.ViewModels.ChoirMembers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ChoraleBackEnd.ViewModels.Events;
using ChoraleBackEnd.ViewModels.Songs;

namespace ChoraleBackEnd.Api.Controllers.AdminControllers;

/// <summary>
/// Administration generale des chorales, transverse a tous les clients (`10-D23`). Les onglets
/// membres/chants/events delegant aux services existants, deja lecture-seule pour un
/// appelant Admin (bypass verifie dans <c>IMembershipService</c>/<c>IAuthorizationService</c>) :
/// aucune duplication de logique d'acces ici.
/// </summary>
[ApiController]
[Authorize(AuthorizationPolicies.Bearer)]
[Authorize(Roles = "Admin")]
[Route("api/admin-choirs")]
public sealed class AdminChoirController(
    IAdminChoirService adminChoirService,
    IChoirMembersService choirMembersService,
    ISongService songService,
    IEventService eventService) : ControllerBase
{
    [HttpPost("GetPaged")]
    public async Task<ActionResult<PagedListViewModel<AdminChoirListItemViewModel>>> GetPaged(
        [FromQuery] AdminChoirsPagedFilterViewModel request, CancellationToken cancellationToken = default)
        => Ok(await adminChoirService.GetPagedAsync(request, cancellationToken));

    [HttpGet("{choirId:guid}")]
    [SpaceReadAudit]
    public async Task<ActionResult<AdminChoirDetailViewModel>> GetById(
        Guid choirId, CancellationToken cancellationToken = default)
        => Ok(await adminChoirService.GetByIdAsync(choirId, cancellationToken));

    [HttpPost("{choirId:guid}/GetMembers")]
    [SpaceReadAudit]
    public async Task<ActionResult<PagedListViewModel<ChoirMemberListItemViewModel>>> GetMembers(
        Guid choirId, [FromQuery] PaginateViewModel pagination, CancellationToken cancellationToken = default)
        => Ok(await choirMembersService.GetPagedAsync(choirId, pagination, cancellationToken));

    [HttpPost("{choirId:guid}/GetSongs")]
    [SpaceReadAudit]
    public async Task<ActionResult<PagedListViewModel<SongViewModel>>> GetSongs(
        Guid choirId, [FromQuery] PaginateViewModel pagination, CancellationToken cancellationToken = default)
        => Ok(await songService.GetPagedByChoirAsync(
            new SongByChoirFilterViewModel
            {
                ChoirId = choirId,
                Page = pagination.Page,
                PageSize = pagination.PageSize,
                Filter = pagination.Filter
            },
            cancellationToken));

    [HttpPost("{choirId:guid}/GetEvents")]
    [SpaceReadAudit]
    public async Task<ActionResult<PagedListViewModel<EventViewModel>>> GetEvents(
        Guid choirId, [FromQuery] PaginateViewModel pagination, CancellationToken cancellationToken = default)
        => Ok(await eventService.GetPagedAsync(
            new EventPagedFilterViewModel
            {
                ChoirId = choirId,
                Page = pagination.Page,
                PageSize = pagination.PageSize,
                Filter = pagination.Filter
            },
            cancellationToken));

    [HttpPut("Update")]
    public async Task<ActionResult<AdminChoirDetailViewModel>> Update(
        [FromBody] AdminChoirUpdateViewModel request, CancellationToken cancellationToken = default)
        => Ok(await adminChoirService.UpdateAsync(request, cancellationToken));

    [HttpGet("{choirId:guid}/ArchiveImpact")]
    public async Task<ActionResult<AdminChoirImpactViewModel>> ArchiveImpact(
        Guid choirId, CancellationToken cancellationToken = default)
        => Ok(await adminChoirService.GetArchiveImpactAsync(choirId, cancellationToken));

    [HttpPut("ChangeStatus")]
    public async Task<ActionResult<AdminChoirDetailViewModel>> ChangeStatus(
        [FromBody] ChangeChoirStatusViewModel request, CancellationToken cancellationToken = default)
        => Ok(await adminChoirService.ChangeStatusAsync(request.Id, request.Status!.Value, cancellationToken));
}
