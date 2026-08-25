using ChoraleBackEnd.Api.Authorization;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.ViewModels.AdminDashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChoraleBackEnd.Api.Controllers.AdminControllers;

/// <summary>
/// Ecran d'accueil de l'administration generale (`10-D30`). Lecture seule, transverse a tous
/// les clients — voir <c>AdminChoirController</c>/<c>AdminEventController</c>.
/// </summary>
[ApiController]
[Authorize(AuthorizationPolicies.Bearer)]
[Authorize(Roles = "Admin")]
[Route("api/admin-dashboard")]
public sealed class AdminDashboardController(IAdminDashboardService adminDashboardService) : ControllerBase
{
    [HttpGet("GetKpi")]
    public async Task<ActionResult<AdminDashboardKpiViewModel>> GetKpi(CancellationToken cancellationToken = default)
        => Ok(await adminDashboardService.GetKpiAsync(cancellationToken));
}
