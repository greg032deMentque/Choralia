using ChoraleBackEnd.Api.Authorization;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.ViewModels.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChoraleBackEnd.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    IAccountService accountService, IRegistrationService registrationService) : ControllerBase
{
    [HttpPost("Register")]
    public async Task<ActionResult<RegistrationResultViewModel>> Register(
        [FromBody] RegisterViewModel request, CancellationToken cancellationToken = default)
        => Ok(await registrationService.RegisterAsync(request, cancellationToken));

    [HttpGet("VerifyEmail")]
    public async Task<IActionResult> VerifyEmail(
        string userId, string token, CancellationToken cancellationToken = default)
    {
        await registrationService.VerifyEmailAsync(userId, token, cancellationToken);
        return NoContent();
    }

    [HttpPost("ResendVerification")]
    public async Task<IActionResult> ResendVerification(
        [FromBody] ResendVerificationViewModel request, CancellationToken cancellationToken = default)
    {
        await registrationService.ResendVerificationAsync(request.Email, cancellationToken);
        return NoContent();
    }

    [HttpPost("Login")]
    public async Task<ActionResult<TokenViewModel>> Login(
        [FromBody] LoginViewModel request, CancellationToken cancellationToken = default)
    {
        var token = await accountService.Login(request);
        if (token is null) return Unauthorized();
        return Ok(token);
    }

    [HttpPost("RefreshToken")]
    public async Task<ActionResult<TokenViewModel>> RefreshToken(
        [FromBody] TokenViewModel request, CancellationToken cancellationToken = default)
    {
        var token = await accountService.RefreshTokenAsync(request);
        if (token is null) return Unauthorized();
        return Ok(token);
    }

    [HttpPost("Logout")]
    [Authorize(AuthorizationPolicies.Bearer)]
    public async Task<IActionResult> Logout(
        [FromBody] LogoutRequestViewModel? request, CancellationToken cancellationToken = default)
    {
        await accountService.Logout(request);
        return NoContent();
    }

    [HttpPost("ForgotPassword")]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] string email, CancellationToken cancellationToken = default)
    {
        await accountService.ForgotPassword(email);
        return NoContent();
    }

    [HttpPost("ResetPassword")]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequestViewModel request, CancellationToken cancellationToken = default)
    {
        var result = await accountService.ResetPassword(request);
        if (!result.Succeeded)
            return BadRequest(result.Errors.Select(e => e.Description));
        return NoContent();
    }

    [HttpPost("ActivateAccount")]
    [AllowAnonymous]
    public async Task<IActionResult> ActivateAccount(
        [FromBody] ActivateAccountViewModel request, CancellationToken cancellationToken = default)
    {
        await accountService.ActivateAccountAsync(request, cancellationToken);
        return NoContent();
    }

    [HttpGet("Me")]
    [Authorize(AuthorizationPolicies.Bearer)]
    public async Task<ActionResult<AuthenticatedUserViewModel>> Me(
        CancellationToken cancellationToken = default)
        => Ok(await accountService.GetCurrentUserAsync(cancellationToken));
}
