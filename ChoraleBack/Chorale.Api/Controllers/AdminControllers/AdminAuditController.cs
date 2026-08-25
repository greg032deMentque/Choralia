using ChoraleBackEnd.Api.Authorization;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.AdminAudit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChoraleBackEnd.Api.Controllers.AdminControllers;

/// <summary>
/// Ecran d'audit de l'administration generale. Lecture seule volontairement : un journal
/// d'audit modifiable ne vaut rien — aucun endpoint d'ecriture ni de suppression n'est exposee.
/// </summary>
[ApiController]
[Authorize(AuthorizationPolicies.Bearer)]
[Authorize(Roles = "Admin")]
[Route("api/admin-audit")]
public sealed class AdminAuditController(IAdminAuditListService adminAuditListService) : ControllerBase
{
    [HttpPost("GetPaged")]
    public async Task<ActionResult<PagedListViewModel<AdminAuditLogListItemViewModel>>> GetPaged(
        [FromQuery] AdminAuditLogPagedFilterViewModel request, CancellationToken cancellationToken = default)
        => Ok(await adminAuditListService.GetPagedAsync(request, cancellationToken));
}
