namespace TripPlanner.Infrastructure.ExternalApis;

/// <summary>
/// Bound from the "Serper" configuration section (options pattern, same as
/// <see cref="GeoapifySettings"/>). The ApiKey lives in the git-ignored
/// .env file — never in a tracked file.
/// </summary>
public class SerperSettings
{
    public const string SectionName = "Serper";

    public string BaseUrl { get; set; } = "https://google.serper.dev/";
    public string ApiKey { get; set; } = string.Empty;
}
