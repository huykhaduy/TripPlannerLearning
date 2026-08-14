using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Application.Common.Interfaces;

namespace TripPlanner.Infrastructure.ExternalApis;

/// <summary>
/// <see cref="IImageSearchProvider"/> backed by Serper's Google Images proxy
/// (https://serper.dev/, base address https://google.serper.dev/, configured
/// in DependencyInjection). Each call spends one paid credit, so callers
/// should cache results rather than re-query for the same term.
///
/// Transient failures are logged here and then rethrown as
/// <see cref="ExternalServiceUnavailableException"/>, not swallowed:
/// DestinationService distinguishes "the search failed" from "the search found
/// nothing" when deciding whether to cache the result, so it must still see them —
/// but as an Application-layer type, not as this adapter's HTTP/JSON exceptions.
/// </summary>
public class SerperImageClient : IImageSearchProvider
{
    private readonly HttpClient _httpClient;
    private readonly SerperSettings _settings;
    private readonly ILogger<SerperImageClient> _logger;

    public SerperImageClient(HttpClient httpClient, IOptions<SerperSettings> settings, ILogger<SerperImageClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<string?> SearchImageAsync(string query, CancellationToken cancellationToken = default) =>
        (await SearchImagesAsync(query, maxResults: 1, cancellationToken)).FirstOrDefault();

    public async Task<IReadOnlyList<string>> SearchImagesAsync(string query, int maxResults, CancellationToken cancellationToken = default)
    {
        // Unconfigured (e.g. a fresh clone without the git-ignored local key) means
        // every call would round-trip to Serper only to get a guaranteed 401 — treat
        // it the same as "nothing found" instead of paying that latency for nothing.
        if (string.IsNullOrEmpty(_settings.ApiKey))
        {
            return [];
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "images")
        {
            Content = JsonContent.Create(new { q = query }),
        };
        request.Headers.Add("X-API-KEY", _settings.ApiKey);

        ImageSearchResponse? payload;
        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            payload = await response.Content.ReadFromJsonAsync<ImageSearchResponse>(cancellationToken);
        }
        catch (Exception ex) when (IsTransientFailure(ex) && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Serper image search failed for {Query}.", query);
            throw new ExternalServiceUnavailableException("Serper image search failed.", ex);
        }

        if (payload?.Images is null)
        {
            return [];
        }

        // Google Image Search almost never comes back with a truly empty result
        // set, even for a nonsense query — so these "top hits" are a best guess,
        // not confirmed matches for the place. Callers must treat them as best-effort.
        return payload.Images
            .Select(i => i.ImageUrl)
            .Where(url => !string.IsNullOrEmpty(url))
            .Take(maxResults)
            .Cast<string>()
            .ToList();
    }

    /// <summary>
    /// Connection failures, HttpClient timeouts (surfaced as TaskCanceledException,
    /// not HttpRequestException), and an unparseable response body.
    /// </summary>
    private static bool IsTransientFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or JsonException;

    private sealed record ImageSearchResponse(
        [property: JsonPropertyName("images")] List<ImageResult>? Images);

    private sealed record ImageResult(
        [property: JsonPropertyName("imageUrl")] string? ImageUrl);
}
