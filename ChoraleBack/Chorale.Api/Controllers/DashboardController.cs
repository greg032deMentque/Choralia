using ChoraleBackEnd.Api.Authorization;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.ViewModels.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChoraleBackEnd.Api.Controllers;

[ApiController]
[Authorize(AuthorizationPolicies.Bearer)]
[Route("api/dashboard")]
public sealed class DashboardController(
    IDashboardService dashboardService,
    ISpaceContextAccessor spaceContextAccessor) : ControllerBase
{
    /// <summary>
    /// Indicateurs de la chorale du scope. Ouvert a tout membre : ce sont des compteurs de
    /// son propre espace, pas des donnees de management.
    /// </summary>
    [HttpGet("ChoirKpi")]
    public async Task<ActionResult<ChoirKpiViewModel>> ChoirKpi(
        CancellationToken cancellationToken = default)
        => Ok(await dashboardService.GetChoirKpiAsync(
            spaceContextAccessor.RequireSpaceId(), cancellationToken));
}
