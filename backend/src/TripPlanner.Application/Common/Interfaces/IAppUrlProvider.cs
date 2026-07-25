namespace TripPlanner.Application.Common.Interfaces;

/// <summary>
/// Exposes app-level URLs that Application services need to build links (e.g.
/// the F4/US2 email-verification link) without depending on Infrastructure's
/// configuration types.
/// </summary>
public interface IAppUrlProvider
{
    /// <summary>Base URL of the deployed frontend, no trailing slash (e.g. "http://localhost:5173").</summary>
    string FrontendBaseUrl { get; }
}
