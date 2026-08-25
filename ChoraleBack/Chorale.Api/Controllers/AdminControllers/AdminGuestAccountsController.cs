using ChoraleBackEnd.Services.UserServices;
using ChoraleBackEnd.ViewModels.Guests;
using ChoraleBackEnd.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChoraleBackEnd.Api.Controllers.AdminControllers;

[ApiController]
[Authorize(AuthorizationPolicies.Bearer)]
[Authorize(Roles = "Admin")]
[Route("api/admin-guest-accounts")]
public sealed class AdminGuestAccountsController(IGuestAccountLifecycleService guestAccountLifecycleService) : ControllerBase
{
    [HttpGet("GetPurgeCandidates")]
    public async Task<ActionResult<PurgeCandidatesViewModel>> GetPurgeCandidates(
        CancellationToken cancellationToken = default)
        => Ok(await guestAccountLifecycleService.GetPurgeCandidatesAsync(cancellationToken));

    [HttpPost("PurgeInactive")]
    public async Task<ActionResult<PurgeGuestsResultViewModel>> PurgeInactive(
        CancellationToken cancellationToken = default)
        => Ok(await guestAccountLifecycleService.PurgeInactiveGuestsAsync(cancellationToken));
}
