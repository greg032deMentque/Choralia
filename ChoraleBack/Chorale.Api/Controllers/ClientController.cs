using ChoraleBackEnd.Api.Authorization;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.Clients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChoraleBackEnd.Api.Controllers;

/// <summary>
/// Administration des clients (`10-D23`).
/// </summary>
/// <remarks>
/// Deux niveaux d'acces. La creation, la modification des plafonds et le changement de
/// statut restent a l'administration generale. La lecture d'un client et la designation
/// d'un responsable sont ouvertes au responsable de ce client — d'ou le `clientId` dans la
/// route : c'est lui que la policy `ClientManager` valide, donc la policy et le service
/// lisent la meme valeur.
/// </remarks>
[ApiController]
[Authorize(AuthorizationPolicies.Bearer)]
[Route("api/clients")]
public sealed class ClientController(IClientService clientService) : ControllerBase
{
    [HttpPost("GetPaged")]
    [Authorize(Roles = nameof(Common.Enums.UserRoleEnum.Admin))]
    public async Task<ActionResult<PagedListViewModel<ClientViewModel>>> GetPaged(
        [FromQuery] ClientsPagedFilterViewModel pagination, CancellationToken cancellationToken = default)
        => Ok(await clientService.GetPagedAsync(pagination, cancellationToken));

    [HttpGet("{clientId:guid}")]
    [Authorize(AuthorizationPolicies.ClientManager)]
    public async Task<ActionResult<ClientViewModel>> GetById(
        Guid clientId, CancellationToken cancellationToken = default)
        => Ok(await clientService.GetByIdAsync(clientId, cancellationToken));

    [HttpGet("{clientId:guid}/SuspensionImpact")]
    [Authorize(Roles = nameof(Common.Enums.UserRoleEnum.Admin))]
    public async Task<ActionResult<SuspensionImpactViewModel>> ImpactSuspension(
        Guid clientId, CancellationToken cancellationToken = default)
        => Ok(await clientService.GetImpactSuspensionAsync(clientId, cancellationToken));

    [HttpPost("Create")]
    [Authorize(Roles = nameof(Common.Enums.UserRoleEnum.Admin))]
    public async Task<ActionResult<ClientViewModel>> Create(
        [FromBody] CreateClientViewModel request, CancellationToken cancellationToken = default)
    {
        var created = await clientService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { clientId = created.Id }, created);
    }

    [HttpPut("Update")]
    [Authorize(Roles = nameof(Common.Enums.UserRoleEnum.Admin))]
    public async Task<ActionResult<ClientViewModel>> Update(
        [FromBody] UpdateClientViewModel request, CancellationToken cancellationToken = default)
        => Ok(await clientService.UpdateAsync(request, cancellationToken));

    [HttpPut("UpdateLimits")]
    [Authorize(Roles = nameof(Common.Enums.UserRoleEnum.Admin))]
    public async Task<ActionResult<ClientViewModel>> UpdateLimits(
        [FromBody] UpdateClientLimitsViewModel request, CancellationToken cancellationToken = default)
        => Ok(await clientService.UpdateLimitsAsync(request, cancellationToken));

    [HttpPut("ChangeStatus")]
    [Authorize(Roles = nameof(Common.Enums.UserRoleEnum.Admin))]
    public async Task<ActionResult<ClientViewModel>> ChangeStatus(
        [FromBody] ChangeClientStatusViewModel request, CancellationToken cancellationToken = default)
        => Ok(await clientService.ChangeStatusAsync(request, cancellationToken));

    [HttpPost("{clientId:guid}/Reactivate")]
    [Authorize(Roles = nameof(Common.Enums.UserRoleEnum.Admin))]
    public async Task<ActionResult<ClientViewModel>> Reactivate(
        Guid clientId, CancellationToken cancellationToken = default)
        => Ok(await clientService.ReactivateAsync(clientId, cancellationToken));

    [HttpPost("{clientId:guid}/GetChoirs")]
    [Authorize(AuthorizationPolicies.ClientManager)]
    public async Task<ActionResult<PagedListViewModel<ClientChoirListItemViewModel>>> GetChoirs(
        Guid clientId, [FromQuery] PaginateViewModel pagination, CancellationToken cancellationToken = default)
        => Ok(await clientService.GetChoirsAsync(clientId, pagination, cancellationToken));

    [HttpGet("{clientId:guid}/Choirs/{choirId:guid}")]
    [Authorize(AuthorizationPolicies.ClientManager)]
    public async Task<ActionResult<ClientChoirDetailViewModel>> GetChoir(
        Guid clientId, Guid choirId, CancellationToken cancellationToken = default)
        => Ok(await clientService.GetChoirAsync(clientId, choirId, cancellationToken));

    [HttpPut("{clientId:guid}/Choirs/{choirId:guid}/ChangeStatus")]
    [Authorize(AuthorizationPolicies.ClientManager)]
    public async Task<ActionResult<ClientChoirDetailViewModel>> ChangeChoirStatus(
        Guid clientId, Guid choirId,
        [FromBody] ChangeClientChoirStatusViewModel request, CancellationToken cancellationToken = default)
        => Ok(await clientService.ChangeChoirStatusAsync(clientId, choirId, request.Status!.Value, cancellationToken));

    [HttpGet("{clientId:guid}/Managers")]
    [Authorize(AuthorizationPolicies.ClientManager)]
    public async Task<ActionResult<PagedListViewModel<ClientManagerListItemViewModel>>> GetManagers(
        Guid clientId, [FromQuery] PaginateViewModel pagination, CancellationToken cancellationToken = default)
        => Ok(await clientService.GetManagersAsync(clientId, pagination, cancellationToken));

    [HttpPost("{clientId:guid}/Managers")]
    [Authorize(AuthorizationPolicies.ClientManager)]
    public async Task<IActionResult> AssignManager(
        Guid clientId,
        [FromBody] AssignClientManagerViewModel request,
        CancellationToken cancellationToken = default)
    {
        await clientService.AssignManagerAsync(clientId, request, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{clientId:guid}/Managers/{userId}")]
    [Authorize(AuthorizationPolicies.ClientManager)]
    public async Task<IActionResult> RemoveManager(
        Guid clientId, string userId, CancellationToken cancellationToken = default)
    {
        await clientService.RemoveManagerAsync(clientId, userId, cancellationToken);
        return NoContent();
    }
}
