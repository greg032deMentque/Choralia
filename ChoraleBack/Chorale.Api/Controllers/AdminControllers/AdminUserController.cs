using ChoraleBackEnd.Services.UserServices;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.AdminUsers;
using ChoraleBackEnd.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChoraleBackEnd.Api.Controllers.AdminControllers;

[ApiController]
[Authorize(AuthorizationPolicies.Bearer)]
[Authorize(Roles = "Admin")]
[Route("api/admin-users")]
public sealed class AdminUserController(
    IAdminUserService adminUserService,
    IAdminUserQueryService adminUserQueryService) : ControllerBase
{
    [HttpPost("GetPaged")]
    public async Task<ActionResult<PagedListViewModel<AdminUserListItemViewModel>>> GetPaged(
        [FromQuery] AdminUsersPagedFilterViewModel request, CancellationToken cancellationToken = default)
        => Ok(await adminUserQueryService.GetPagedAsync(request, cancellationToken));

    [HttpPost("Create")]
    public async Task<ActionResult<AdminUserListItemViewModel>> Create(
        [FromBody] CreateAdminUserViewModel request, CancellationToken cancellationToken = default)
    {
        var created = await adminUserService.CreateAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpPost("GetChoirUsersPaged")]
    public async Task<ActionResult<PagedListViewModel<AdminChoirUserListItemViewModel>>> GetChoirUsersPaged(
        [FromQuery] AdminChoirUsersPagedFilterViewModel request, CancellationToken cancellationToken = default)
        => Ok(await adminUserQueryService.GetChoirUsersPagedAsync(request, cancellationToken));

    [HttpPost("GetEventUsersPaged")]
    public async Task<ActionResult<PagedListViewModel<AdminEventUserListItemViewModel>>> GetEventUsersPaged(
        [FromQuery] AdminEventUsersPagedFilterViewModel request, CancellationToken cancellationToken = default)
        => Ok(await adminUserQueryService.GetEventUsersPagedAsync(request, cancellationToken));

    [HttpPost("GetUnattachedUsersPaged")]
    public async Task<ActionResult<PagedListViewModel<AdminUnattachedUserListItemViewModel>>> GetUnattachedUsersPaged(
        [FromQuery] AdminUsersPagedFilterViewModel request, CancellationToken cancellationToken = default)
        => Ok(await adminUserQueryService.GetUnattachedUsersPagedAsync(request, cancellationToken));

    [HttpGet("GetUserDetail")]
    public async Task<ActionResult<AdminUserDetailViewModel>> GetUserDetail(
        string userId, CancellationToken cancellationToken = default)
        => Ok(await adminUserQueryService.GetUserDetailAsync(userId, cancellationToken));

    [HttpPut("UpdateIdentity")]
    public async Task<ActionResult<AdminUserDetailViewModel>> UpdateIdentity(
        [FromBody] AdminUserUpdateIdentityViewModel request, CancellationToken cancellationToken = default)
        => Ok(await adminUserService.UpdateIdentityAsync(request, cancellationToken));

    [HttpPut("SetActive")]
    public async Task<ActionResult<AdminUserDetailViewModel>> SetActive(
        [FromBody] AdminUserSetActiveViewModel request, CancellationToken cancellationToken = default)
        => Ok(await adminUserService.SetActiveAsync(request.UserId, request.IsActive!.Value, cancellationToken));

    [HttpPost("ResetPassword")]
    public async Task<IActionResult> ResetPassword(string userId, CancellationToken cancellationToken = default)
    {
        await adminUserService.ResetPasswordAsync(userId, cancellationToken);
        return NoContent();
    }

    [HttpPost("ResendInvitation")]
    public async Task<IActionResult> ResendInvitation(string userId, CancellationToken cancellationToken = default)
    {
        await adminUserService.ResendInvitationAsync(userId, cancellationToken);
        return NoContent();
    }

    [HttpDelete("Delete")]
    public async Task<IActionResult> Delete(string userId, CancellationToken cancellationToken = default)
    {
        await adminUserService.DeleteAsync(userId, cancellationToken);
        return NoContent();
    }
}
