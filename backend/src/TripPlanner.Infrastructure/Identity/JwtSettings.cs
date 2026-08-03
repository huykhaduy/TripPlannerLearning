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

    /// <summary>
    /// HMAC-SHA256 (the algorithm JwtTokenGenerator signs with) requires a key
    /// of at least 256 bits, so anything shorter than 32 bytes is rejected at
    /// startup rather than failing on the first login. Enforced in Program.cs.
    /// </summary>
    public const int MinKeyBytes = 32;

    public string Issuer { get; set; } = DefaultIssuer;
    public string Audience { get; set; } = DefaultAudience;

    /// <summary>Signing secret, required, ≥32 chars — set only via .env (Jwt__Key), never defaulted or committed.</summary>
    public string Key { get; set; } = string.Empty;

    public int ExpiryMinutes { get; set; } = 60;
}
