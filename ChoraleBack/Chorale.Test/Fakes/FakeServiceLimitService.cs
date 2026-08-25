using System;
using System.Threading;
using System.Threading.Tasks;
using ChoraleBackEnd.Services.ClientServices;

namespace ChoraleBackEnd.Test.Fakes;

/// <summary>
/// Autorise tout. Les suites qui l'utilisent testent autre chose que les plafonds ; les
/// limites de service ont leur propre suite dediee.
/// </summary>
public sealed class FakeServiceLimitService : IServiceLimitService
{
    public Task EnsureCanCreateChoirAsync(Guid clientId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task EnsureCanAddMemberAsync(Guid choirId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task EnsureCanAddMemberToNewChoirAsync(Guid clientId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task EnsureCanUploadFileAsync(
        Guid choirId, long sizeBytes, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<ClientUsage> GetUsageAsync(
        Guid clientId, CancellationToken ct = default)
        => Task.FromResult(new ClientUsage(0, 0, 0, 0, 0, 0, 0));
}
