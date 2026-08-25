using ChoraleBackEnd.Api.Authorization;
using ChoraleBackEnd.Services.OnboardingServices;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.Onboarding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ChoraleBackEnd.ViewModels.Events;
using ChoraleBackEnd.ViewModels.Choirs;

namespace ChoraleBackEnd.Api.Controllers;

[ApiController]
[Authorize(AuthorizationPolicies.Bearer)]
[Route("api/onboarding")]
public sealed class OnboardingController(
    IJoinCodeService joinCodeService,
    IMembershipRequestService membershipRequestService,
    IOnboardingCreationService onboardingCreationService) : ControllerBase
{
    [HttpGet("PreviewCode")]
    [AllowAnonymous]
    public async Task<ActionResult<PreviewCodeViewModel>> PreviewCode(
        string code, CancellationToken cancellationToken = default)
        => Ok(await joinCodeService.PreviewAsync(code, cancellationToken));

    [HttpPost("RequestMembership")]
    public async Task<ActionResult<MyRequestViewModel>> RequestMembership(
        [FromBody] RequestMembershipViewModel request, CancellationToken cancellationToken = default)
        => Ok(await membershipRequestService.RequestMembershipAsync(request, cancellationToken));

    [HttpPost("CreateChoir")]
    public async Task<ActionResult<ChoirViewModel>> CreateChoir(
        [FromBody] CreateChoirViewModel request, CancellationToken cancellationToken = default)
        => Ok(await onboardingCreationService.CreateChoirAsync(request, cancellationToken));

    [HttpPost("CreateEvent")]
    public async Task<ActionResult<EventViewModel>> CreateEvent(
        [FromBody] CreateEventViewModel request, CancellationToken cancellationToken = default)
        => Ok(await onboardingCreationService.CreateEventAsync(request, cancellationToken));

    [HttpGet("MyRequests")]
    public async Task<ActionResult<PagedListViewModel<MyRequestViewModel>>> MyRequests(
        [FromQuery] PaginateViewModel pagination, CancellationToken cancellationToken = default)
        => Ok(await membershipRequestService.MyRequestsAsync(pagination, cancellationToken));

    [HttpDelete("MyRequests/{id:guid}")]
    public async Task<IActionResult> CancelRequest(
        Guid id, CancellationToken cancellationToken = default)
    {
        await membershipRequestService.CancelAsync(id, cancellationToken);
        return NoContent();
    }
}
