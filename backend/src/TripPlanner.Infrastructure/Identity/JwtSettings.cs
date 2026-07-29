namespace TripPlanner.Infrastructure.Identity;

/// <summary>
/// Strongly-typed JWT configuration, bound from the "Jwt" section via the
/// options pattern. Issuer/Audience/ExpiryMinutes default to sensible values
/// below; Key has no default — it's a secret and must come from the
/// git-ignored .env file (Jwt__Key).
/// </summary>
public class JwtSettings
{
    public const string SectionName = "Jwt";
    public const string DefaultIssuer = "TripPlanner";
    public const string DefaultAudience = "TripPlannerClient";

    public string Issuer { get; set; } = DefaultIssuer;
    public string Audience { get; set; } = DefaultAudience;

    /// <summary>Signing secret, required, ≥32 chars — set only via .env (Jwt__Key), never defaulted or committed.</summary>
    public string Key { get; set; } = string.Empty;

    public int ExpiryMinutes { get; set; } = 60;
}
