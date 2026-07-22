using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TripPlanner.Application.Common.Interfaces;

namespace TripPlanner.Infrastructure.ExternalApis;

/// <summary>
/// <see cref="IImageSearchProvider"/> backed by Serper's Google Images proxy
/// (https://serper.dev/, base address https://google.serper.dev/, configured
/// in DependencyInjection). Each call spends one paid credit, so callers
/// should cache results rather than re-query for the same term.
/// </summary>
public class SerperImageClient : IImageSearchProvider
{
    private readonly HttpClient _httpClient;
    private readonly SerperSettings _settings;

    public SerperImageClient(HttpClient httpClient, IOptions<SerperSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
    }

    public async Task<string?> SearchImageAsync(string query, CancellationToken cancellationToken = default)
    {
        // Unconfigured (e.g. a fresh clone without the git-ignored local key) means
        // every call would round-trip to Serper only to get a guaranteed 401 — treat
        // it the same as "nothing found" instead of paying that latency for nothing.
        if (string.IsNullOrEmpty(_settings.ApiKey))
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "images")
        {
            Content = JsonContent.Create(new { q = query }),
        };
        request.Headers.Add("X-API-KEY", _settings.ApiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ImageSearchResponse>(cancellationToken);

        // Google Image Search almost never comes back with a truly empty result
        // set, even for a nonsense query — so the "top hit" is a best guess, not
        // a confirmed match for the place. Callers must treat it as best-effort.
        return payload?.Images?.FirstOrDefault()?.ImageUrl;
    }

    private sealed record ImageSearchResponse(
        [property: JsonPropertyName("images")] List<ImageResult>? Images);

    private sealed record ImageResult(
        [property: JsonPropertyName("imageUrl")] string? ImageUrl);
}
