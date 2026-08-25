using ChoraleBackEnd.Services.Technical;

namespace ChoraleBackEnd.Test.Fakes;

public sealed class ThrowingFakeEmailService : IEmailService
{
    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
        => throw new InvalidOperationException("Échec simulé d'envoi SMTP.");
}
