namespace TripPlanner.Infrastructure.Email;

/// <summary>
/// Bound from the "Smtp" configuration section (options pattern, same as
/// GeoapifySettings/SerperSettings). User/AppPassword are git-ignored secrets
/// loaded from .env — see .env.example. Gmail requires a Google Account App
/// Password (myaccount.google.com/apppasswords, needs 2-Step Verification
/// enabled first) — your normal account password is rejected by smtp.gmail.com.
/// </summary>
public class SmtpSettings
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public string User { get; set; } = string.Empty;
    public string AppPassword { get; set; } = string.Empty;
}
