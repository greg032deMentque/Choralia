using ChoraleBackEnd.Api.Authorization;
using ChoraleBackEnd.Services.OnboardingServices;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.Onboarding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChoraleBackEnd.Api.Controllers;

/// <summary>
/// File des demandes d'adhesion d'un espace. La policy HTTP <c>ChoirManager</c> (role
/// Responsable, resolu sur <c>spaceId</c> depuis la route) est une premiere barriere ; le
/// service revalide le meme role — matrice `02`, ni Organizer ni SectionLeader.
/// </summary>
[ApiController]
[Authorize(AuthorizationPolicies.Bearer)]
[Authorize(AuthorizationPolicies.ChoirManager)]
[Route("api/spaces/{spaceId:guid}/MembershipRequests")]
public sealed class SpaceRequestsController(IMembershipRequestService membershipRequestService) : ControllerBase
{
    [HttpPost("GetPaged")]
    public async Task<ActionResult<PagedListViewModel<MembershipRequestListItemViewModel>>> GetPaged(
        Guid spaceId, [FromQuery] PaginateViewModel pagination, CancellationToken cancellationToken = default)
        => Ok(await membershipRequestService.GetPagedAsync(spaceId, pagination, cancellationToken));

    [HttpPost("{id:guid}/Approve")]
    public async Task<ActionResult<MembershipRequestListItemViewModel>> Approve(
        Guid spaceId, Guid id, [FromBody] ApproveRequestViewModel request,
        CancellationToken cancellationToken = default)
        => Ok(await membershipRequestService.ApproveAsync(spaceId, id, request, cancellationToken));

    [HttpPost("{id:guid}/Decline")]
    public async Task<ActionResult<MembershipRequestListItemViewModel>> Decline(
        Guid spaceId, Guid id, [FromBody] DeclineRequestViewModel request,
        CancellationToken cancellationToken = default)
        => Ok(await membershipRequestService.DeclineAsync(spaceId, id, request, cancellationToken));
}
