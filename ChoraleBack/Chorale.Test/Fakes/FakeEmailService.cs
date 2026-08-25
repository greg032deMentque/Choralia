using ChoraleBackEnd.Services.Technical;

namespace ChoraleBackEnd.Test.Fakes;

public sealed class FakeEmailService : IEmailService
{
    public List<(string To, string Subject, string HtmlBody)> SentEmails { get; } = [];

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        SentEmails.Add((to, subject, htmlBody));
        return Task.CompletedTask;
    }
}
