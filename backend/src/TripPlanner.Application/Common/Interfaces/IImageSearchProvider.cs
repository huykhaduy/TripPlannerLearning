namespace TripPlanner.Application.Common.Interfaces;

/// <summary>
/// Abstraction over a general-purpose image search provider (e.g. Serper's
/// Google Images proxy), used to fill in attraction thumbnails that the
/// destination provider itself doesn't return.
/// </summary>
public interface IImageSearchProvider
{
    /// <summary>Best-effort image lookup by free-text query. Null if nothing was found.</summary>
    Task<string?> SearchImageAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Best-effort multi-image lookup by free-text query, capped at
    /// <paramref name="maxResults"/>. Empty list if nothing was found.
    /// </summary>
    Task<IReadOnlyList<string>> SearchImagesAsync(string query, int maxResults, CancellationToken cancellationToken = default);
}
