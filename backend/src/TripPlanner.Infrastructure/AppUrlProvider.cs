using Microsoft.Extensions.Configuration;
using TripPlanner.Application.Common.Interfaces;

namespace TripPlanner.Infrastructure;

/// <summary>
/// Reads "App:FrontendBaseUrl" directly (not the options pattern — it's one
/// plain string, not a group of related settings like SmtpSettings/JwtSettings).
/// </summary>
public class AppUrlProvider : IAppUrlProvider
{
    public AppUrlProvider(IConfiguration configuration)
    {
        FrontendBaseUrl = (configuration["App:FrontendBaseUrl"] ?? "http://localhost:5173").TrimEnd('/');
    }

    public string FrontendBaseUrl { get; }
}
