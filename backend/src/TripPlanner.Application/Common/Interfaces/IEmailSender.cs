namespace TripPlanner.Application.Common.Interfaces;

/// <summary>
/// Abstraction over sending transactional email (e.g. the F4/US2 email-verification
/// link). Implemented in Infrastructure via SMTP.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default);
}
