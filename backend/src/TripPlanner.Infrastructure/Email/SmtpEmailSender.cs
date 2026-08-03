using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TripPlanner.Application.Common.Interfaces;

namespace TripPlanner.Infrastructure.Email;

/// <summary>
/// <see cref="IEmailSender"/> over Gmail's SMTP relay (smtp.gmail.com:587,
/// STARTTLS). See <see cref="SmtpSettings"/> for the App Password requirement.
///
/// A transient failure is logged here and then RETHROWN. Swallowing it would move
/// the "registration succeeds even when mail is down" guarantee (F4/US1) out of
/// AuthService, where a test asserts it, and into this class, where nothing does.
/// </summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpSettings _settings;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<SmtpSettings> settings, ILogger<SmtpEmailSender> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        // Unconfigured (e.g. a fresh clone without the git-ignored local
        // credentials) — MailMessage's constructor throws ArgumentException on
        // an empty "from" address, so this must be checked before sending.
        // Debug, not Warning: on a fresh clone this is expected, not a fault.
        if (string.IsNullOrEmpty(_settings.User) || string.IsNullOrEmpty(_settings.AppPassword))
        {
            _logger.LogDebug("SMTP is not configured; skipping email {Subject}.", subject);
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

        try
        {
            await client.SendMailAsync(message, cancellationToken);
        }
        catch (Exception ex) when (IsTransientFailure(ex) && !cancellationToken.IsCancellationRequested)
        {
            // The recipient address is deliberately NOT logged — it is personal
            // data, and the exception alone is enough to diagnose an outage.
            _logger.LogWarning(ex, "Failed to send email {Subject} over SMTP.", subject);
            throw;
        }
    }

    // SmtpFailedRecipientException derives from SmtpException.
    private static bool IsTransientFailure(Exception ex) =>
        ex is SmtpException or SocketException;
}
