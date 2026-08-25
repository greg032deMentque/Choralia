using ChoraleBackEnd.Api.Authorization;
using ChoraleBackEnd.Services.OnboardingServices;
using ChoraleBackEnd.ViewModels.Onboarding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChoraleBackEnd.Api.Controllers;

[ApiController]
[Authorize(AuthorizationPolicies.Bearer)]
[Authorize(AuthorizationPolicies.SpaceManager)]
[Route("api/spaces/{spaceId:guid}/JoinCode")]
public sealed class SpaceJoinCodeController(IJoinCodeService joinCodeService)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<JoinCodeViewModel>> Get(
        Guid spaceId, CancellationToken cancellationToken = default)
        => Ok(await joinCodeService.GetActiveAsync(spaceId, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<JoinCodeViewModel>> GenerateOrRotate(
        Guid spaceId, [FromQuery] int? durationDays = null, CancellationToken cancellationToken = default)
        => Ok(await joinCodeService.GenerateOrRotateAsync(spaceId, durationDays, cancellationToken));

    [HttpDelete]
    public async Task<IActionResult> Deactivate(
        Guid spaceId, CancellationToken cancellationToken = default)
    {
        await joinCodeService.DeactivateAsync(spaceId, cancellationToken);
        return NoContent();
    }
}
