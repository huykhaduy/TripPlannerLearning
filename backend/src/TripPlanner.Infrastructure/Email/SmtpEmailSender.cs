using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using TripPlanner.Application.Common.Interfaces;

namespace TripPlanner.Infrastructure.Email;

/// <summary>
/// <see cref="IEmailSender"/> over Gmail's SMTP relay (smtp.gmail.com:587,
/// STARTTLS). See <see cref="SmtpSettings"/> for the App Password requirement.
/// </summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpSettings _settings;

    public SmtpEmailSender(IOptions<SmtpSettings> settings)
    {
        _settings = settings.Value;
    }

    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        // Unconfigured (e.g. a fresh clone without the git-ignored local
        // credentials) — MailMessage's constructor throws ArgumentException on
        // an empty "from" address, which isn't a transient-failure type the
        // caller catches, so this must be checked before attempting to send.
        if (string.IsNullOrEmpty(_settings.User) || string.IsNullOrEmpty(_settings.AppPassword))
        {
            return;
        }

        using var client = new SmtpClient(_settings.Host, _settings.Port)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(_settings.User, _settings.AppPassword),
        };
        using var message = new MailMessage(_settings.User, toEmail, subject, htmlBody)
        {
            IsBodyHtml = true,
        };

        await client.SendMailAsync(message, cancellationToken);
    }
}
