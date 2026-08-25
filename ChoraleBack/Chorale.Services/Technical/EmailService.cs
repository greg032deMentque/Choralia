using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace ChoraleBackEnd.Services.Technical;

public interface IEmailService
{
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
}

public sealed class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogService _logger;

    public EmailService(IConfiguration configuration, ILogService logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        var host = _configuration["Smtp:Host"]
            ?? throw new InvalidOperationException("Smtp:Host manquant.");
        var port = _configuration.GetValue<int>("Smtp:Port");
        var from = _configuration["Smtp:From"]
            ?? throw new InvalidOperationException("Smtp:From manquant.");
        var fromName = _configuration["Smtp:FromName"];
        var userName = _configuration["Smtp:UserName"];
        var password = _configuration["Smtp:Password"];
        var enableSsl = _configuration.GetValue<bool>("Smtp:EnableSsl");

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = enableSsl,
            Credentials = string.IsNullOrWhiteSpace(userName)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(userName, password)
        };

        using var message = new MailMessage(BuildFromAddress(from, fromName), new MailAddress(to))
        {
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };

        try
        {
            await client.SendMailAsync(message, ct);
        }
        catch (SmtpException ex)
        {
            _logger.LogError("Échec d'envoi d'email SMTP.", ex);
            throw;
        }
    }

    public static MailAddress BuildFromAddress(string from, string? fromName)
        => string.IsNullOrWhiteSpace(fromName)
            ? new MailAddress(from)
            : new MailAddress(from, fromName, Encoding.UTF8);
}
