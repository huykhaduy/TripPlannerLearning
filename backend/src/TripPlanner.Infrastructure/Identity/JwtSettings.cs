namespace TripPlanner.Infrastructure.Identity;

/// <summary>
/// Strongly-typed JWT configuration, bound from the "Jwt" section of
/// appsettings.json via the options pattern.
/// </summary>
public class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;

    /// <summary>Signing secret. Keep it long (≥32 chars) and out of source control in real apps.</summary>
    public string Key { get; set; } = string.Empty;

    public int ExpiryMinutes { get; set; } = 60;
}
