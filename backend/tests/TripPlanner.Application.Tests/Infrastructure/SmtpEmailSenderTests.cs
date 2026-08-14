using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TripPlanner.Application.Tests.TestDoubles;
using TripPlanner.Infrastructure.Email;
using Xunit;

namespace TripPlanner.Application.Tests.Infrastructure;

/// <summary>
/// There is no SMTP server here, so what is testable is the branch taken *before*
/// one is contacted: an unconfigured sender must return quietly rather than throw,
/// and a configured one must actually try. Both matter — the first is the state of
/// every fresh clone, and it is what stops registration failing on a machine with
/// no mail credentials.
/// </summary>
public class SmtpEmailSenderTests
{
    private static SmtpEmailSender CreateSut(SmtpSettings settings, ILogger<SmtpEmailSender> logger) =>
        new(Options.Create(settings), logger);

    [Fact]
    public async Task WithNoCredentials_ItSkipsTheSendInsteadOfThrowing()
    {
        // MailMessage's constructor throws ArgumentException on an empty "from"
        // address, so this branch has to come before the message is built — not
        // after. Reordering it would turn every registration on a fresh clone
        // into a 500.
        var logger = new RecordingLogger<SmtpEmailSender>();
        var sut = CreateSut(new SmtpSettings(), logger);

        await sut.SendAsync("ada@example.com", "Verify your email", "<p>link</p>");

        Assert.Single(logger.Entries);
    }

    [Fact]
    public async Task WithAUserButNoAppPassword_ItAlsoSkips()
    {
        // Half-configured is still unconfigured; Gmail rejects an empty password.
        var logger = new RecordingLogger<SmtpEmailSender>();
        var sut = CreateSut(new SmtpSettings { User = "sender@example.com" }, logger);

        await sut.SendAsync("ada@example.com", "Verify your email", "<p>link</p>");

        Assert.Single(logger.Entries);
    }

    [Fact]
    public async Task WithAnAppPasswordButNoUser_ItAlsoSkips()
    {
        var logger = new RecordingLogger<SmtpEmailSender>();
        var sut = CreateSut(new SmtpSettings { AppPassword = "app-password" }, logger);

        await sut.SendAsync("ada@example.com", "Verify your email", "<p>link</p>");

        Assert.Single(logger.Entries);
    }

    [Fact]
    public async Task SkippingIsLoggedAtDebug_NotAsAWarning()
    {
        // On a fresh clone this is expected, not a fault — logging it at Warning
        // would train everyone to ignore this sender's warnings.
        var logger = new RecordingLogger<SmtpEmailSender>();
        var sut = CreateSut(new SmtpSettings(), logger);

        await sut.SendAsync("ada@example.com", "Verify your email", "<p>link</p>");

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Debug, entry.Level);
        Assert.Contains("Verify your email", entry.Message);
        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public async Task TheRecipientAddressIsNeverLogged()
    {
        // It is personal data, and the subject plus the exception are enough to
        // diagnose a mail outage without it.
        var logger = new RecordingLogger<SmtpEmailSender>();
        var sut = CreateSut(new SmtpSettings(), logger);

        await sut.SendAsync("ada@example.com", "Verify your email", "<p>link</p>");

        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("ada@example.com"));
    }

    [Fact]
    public async Task TheAppPasswordIsNeverLogged()
    {
        var logger = new RecordingLogger<SmtpEmailSender>();
        var sut = CreateSut(new SmtpSettings { User = "sender@example.com" }, logger);

        await sut.SendAsync("ada@example.com", "Verify your email", "<p>link</p>");

        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("app-password"));
    }

    [Fact]
    public async Task WhenConfigured_ItTriesToReachTheConfiguredHostAndFailsLoudly()
    {
        // Paired with the skip tests above: without this, a sender that skipped
        // unconditionally would pass every one of them. Port 1 has nothing
        // listening, so a real connection attempt fails fast — and the failure
        // must surface rather than be swallowed, because AuthService (not this
        // class) owns the "registration survives a mail outage" guarantee.
        var logger = new RecordingLogger<SmtpEmailSender>();
        var sut = CreateSut(
            new SmtpSettings
            {
                Host = "127.0.0.1",
                Port = 1,
                User = "sender@example.com",
                AppPassword = "app-password",
            },
            logger);

        await Assert.ThrowsAnyAsync<Exception>(
            () => sut.SendAsync("ada@example.com", "Verify your email", "<p>link</p>"));
    }

    [Fact]
    public void TheDefaultsPointAtGmailsStartTlsRelay()
    {
        // These are the only settings with no .env entry in the common case, so a
        // silent change to them would break mail for everyone who never set them.
        var settings = new SmtpSettings();

        Assert.Equal("smtp.gmail.com", settings.Host);
        Assert.Equal(587, settings.Port);
        Assert.Equal(string.Empty, settings.User);
        Assert.Equal(string.Empty, settings.AppPassword);
    }
}
