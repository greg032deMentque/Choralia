using ChoraleBackEnd.Services.AuthServices;
using Microsoft.AspNetCore.Identity;
using ChoraleBackEnd.ViewModels.Auth;

namespace ChoraleBackEnd.Test.Fakes;

public sealed class FakeAccountService : IAccountService
{
    public List<string> ForgotPasswordCalls { get; } = [];

    public Task<TokenViewModel?> Login(LoginViewModel model) => Task.FromResult<TokenViewModel?>(null);

    public Task<TokenViewModel?> RefreshTokenAsync(TokenViewModel request) => Task.FromResult<TokenViewModel?>(null);

    public Task UnlockUser(string userId) => Task.CompletedTask;

    public Task<IdentityResult> ResetPassword(ResetPasswordRequestViewModel model) => Task.FromResult(IdentityResult.Success);

    public Task ActivateAccountAsync(ActivateAccountViewModel model, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task Logout(LogoutRequestViewModel? request) => Task.CompletedTask;

    public Task ForgotPassword(string email)
    {
        ForgotPasswordCalls.Add(email);
        return Task.CompletedTask;
    }

    public Task<AuthenticatedUserViewModel> GetCurrentUserAsync(CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
