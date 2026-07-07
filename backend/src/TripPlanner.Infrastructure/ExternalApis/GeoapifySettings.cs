namespace TripPlanner.Infrastructure.ExternalApis;

/// <summary>
/// Bound from the "Geoapify" configuration section (options pattern, same as
/// <see cref="Identity.JwtSettings"/>). The ApiKey lives in the git-ignored
/// appsettings.Development.local.json — never in a tracked file.
/// </summary>
public class GeoapifySettings
{
    public const string SectionName = "Geoapify";

    public string BaseUrl { get; set; } = "https://api.geoapify.com/";
    public string ApiKey { get; set; } = string.Empty;
}
