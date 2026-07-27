using System.Collections.Concurrent;
using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Application.Common.Validation;
using TripPlanner.Application.Features.Destinations.Dtos;
using TripPlanner.Domain.Entities;

namespace TripPlanner.Application.Features.Destinations;

/// <summary>
/// Destination search, attractions, and details (Features 1 &amp; 2).
///
/// Calls <see cref="IDestinationProvider"/> for raw data, de-duplicates and
/// ranks/caps results per the F1 business rules, and maps provider models to
/// the DTOs in this folder. <see cref="Destination"/> rows are a cache of
/// external-provider data keyed by <c>ProviderId</c> — this service never
/// persists a new row itself; see <see cref="Trips.TripService.GetOrCreateDestinationAsync"/>
/// for the upsert-on-first-add path.
/// </summary>
public class DestinationService : IDestinationService
{
    private const int MaxLocationResults = 5;
    private const int MaxAttractionResults = 20;

    // The destination provider's own image data is sparse (Geoapify only has
    // wiki_and_media for some places), so filling in thumbnails means one
    // extra image-search lookup per result — capped in parallel so a
    // 20-result page doesn't hammer the image search provider (a paid,
    // per-query API) all at once.
    private const int ImageFetchConcurrency = 5;

    // F2/US2 — how many photos the details view's carousel gets per place.
    private const int DetailsPhotoCount = 5;

    // How long a resolved (or "nothing found") image is cached per place, so
    // a paid Serper query is never repeated for the same attraction.
    private static readonly TimeSpan ImageTtl = TimeSpan.FromHours(24);

    // TTLs per spec §11.2 — POI data is nearly static.
    private static readonly TimeSpan LocationsTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan AttractionsTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan DetailsTtl = TimeSpan.FromHours(24);

    // Entries stay resident well past their TTL so a provider outage can be
    // answered with stale data (spec §11.2 "stale-better-than-down"); the
    // retention window bounds memory growth.
    private static readonly TimeSpan CacheRetention = TimeSpan.FromDays(7);

    private readonly IDestinationRepository _destinations;
    private readonly IDestinationProvider _provider;
    private readonly IImageSearchProvider _imageSearch;
    private readonly IDistributedCache _cache;
    private readonly TimeProvider _clock;
    private readonly IValidator<SearchLocationsRequest> _searchValidator;
    private readonly IValidator<GetAttractionsRequest> _attractionsValidator;
    private readonly IValidator<GetDestinationDetailsRequest> _detailsValidator;

    public DestinationService(
        IDestinationRepository destinations,
        IDestinationProvider provider,
        IImageSearchProvider imageSearch,
        IDistributedCache cache,
        TimeProvider clock,
        IValidator<SearchLocationsRequest> searchValidator,
        IValidator<GetAttractionsRequest> attractionsValidator,
        IValidator<GetDestinationDetailsRequest> detailsValidator)
    {
        _destinations = destinations;
        _provider = provider;
        _imageSearch = imageSearch;
        _cache = cache;
        _clock = clock;
        _searchValidator = searchValidator;
        _attractionsValidator = attractionsValidator;
        _detailsValidator = detailsValidator;
    }

    /// <summary>
    /// A cached value plus WHEN it was fetched. Freshness is checked against
    /// the TTL by hand (instead of letting the cache evict) precisely so an
    /// expired entry is still readable as a stale fallback during an outage.
    /// </summary>
    private sealed record CacheEnvelope<T>(T Value, DateTimeOffset FetchedAt);

    /// <summary>
    /// Reads and JSON-deserializes a <see cref="CacheEnvelope{T}"/> from
    /// <see cref="IDistributedCache"/>. A connectivity failure (Redis down or
    /// unreachable) is treated exactly like a cache miss — never surfaced to
    /// the caller — per the "degrade gracefully" design decision. Also treats
    /// a <see cref="JsonException"/> as a miss: <see cref="CacheRetention"/> is
    /// 7 days, long enough for a student to reshape a DTO in
    /// <c>Features/Destinations/Dtos/</c> between restarts, so a stale entry
    /// that no longer matches the current shape must not 500 the request —
    /// same treatment <see cref="IsTransientExternalFailure"/> already gives
    /// <see cref="JsonException"/> elsewhere in this file. Deliberately NOT
    /// applied in <see cref="SetCachedEnvelopeAsync{T}"/>: a serialize failure
    /// on write is a real bug and should surface, not be swallowed.
    /// </summary>
    private async Task<CacheEnvelope<T>?> TryGetCachedEnvelopeAsync<T>(string key, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await _cache.GetAsync(key, cancellationToken);
            return bytes is null ? null : JsonSerializer.Deserialize<CacheEnvelope<T>>(bytes);
        }
        catch (Exception ex) when (IsCacheUnavailable(ex) || ex is JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// JSON-serializes and writes a <see cref="CacheEnvelope{T}"/> to
    /// <see cref="IDistributedCache"/>, retained for <see cref="CacheRetention"/>.
    /// A connectivity failure is swallowed — the freshly-fetched value the
    /// caller already has is still returned; this round just isn't cached.
    /// </summary>
    private async Task SetCachedEnvelopeAsync<T>(string key, CacheEnvelope<T> envelope, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
            await _cache.SetAsync(
                key, bytes,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheRetention },
                cancellationToken);
        }
        catch (Exception ex) when (IsCacheUnavailable(ex))
        {
        }
    }

    private static bool IsCacheUnavailable(Exception ex) =>
        ex is RedisConnectionException or RedisTimeoutException or TimeoutException;

    /// <summary>
    /// Cache-aside over the provider (NFR1/NFR2): fresh hit → no provider
    /// call; miss/expired → fetch and re-cache; provider down but a stale
    /// entry exists → serve it rather than fail (spec §11.2).
    /// </summary>
    private async Task<T> GetCachedAsync<T>(string key, TimeSpan ttl, Func<Task<T>> fetchAsync, CancellationToken cancellationToken)
    {
        var stale = await TryGetCachedEnvelopeAsync<T>(key, cancellationToken);
        if (stale is not null && _clock.GetUtcNow() - stale.FetchedAt < ttl)
        {
            return stale.Value;
        }

        try
        {
            var value = await fetchAsync();
            await SetCachedEnvelopeAsync(key, new CacheEnvelope<T>(value, _clock.GetUtcNow()), cancellationToken);
            return value;
        }
        catch (HttpRequestException) when (stale is not null)
        {
            return stale.Value; // stale-better-than-down
        }
    }

    public async Task<IReadOnlyList<LocationSuggestionDto>> SearchLocationsAsync(string query, CancellationToken cancellationToken = default)
    {
        // F1/US1-US2 — the TRIMMED query must be ≥2 chars (spec §11.2), so
        // validate the same string we send to the provider, not the raw input.
        var trimmedQuery = query?.Trim() ?? string.Empty;
        await _searchValidator.ValidateAndThrowAppExceptionAsync(new SearchLocationsRequest(trimmedQuery), cancellationToken);

        // Key is the lowered query so "Paris" and "paris" share one entry. The
        // cached value is the POST-processed list — dedupe/rank/cap are
        // deterministic, so a hit skips that work too.
        var loweredQuery = trimmedQuery.ToLowerInvariant();
        return await GetCachedAsync<IReadOnlyList<LocationSuggestionDto>>(
            $"loc:{loweredQuery}", LocationsTtl, async () =>
            {
                var suggestions = await _provider.SearchLocationsAsync(trimmedQuery, cancellationToken);

                // Geoapify can return the same city under several place ids — dedupe on
                // what the user actually sees (name + country), rank exact and prefix
                // matches above substring hits (F1/US2 business rule), then cap at 5.
                return suggestions
                    .DistinctBy(s => (s.Name.ToLowerInvariant(), s.Country?.ToLowerInvariant()))
                    .OrderBy(s => RelevanceRank(s.Name, loweredQuery))
                    .Take(MaxLocationResults)
                    .ToList();
            }, cancellationToken);
    }

    /// <summary>Exact match first, then prefix matches, then everything else.</summary>
    private static int RelevanceRank(string name, string loweredQuery)
    {
        var loweredName = name.ToLowerInvariant();
        if (loweredName == loweredQuery)
        {
            return 0;
        }

        return loweredName.StartsWith(loweredQuery, StringComparison.Ordinal) ? 1 : 2;
    }

    public async Task<IReadOnlyList<DestinationSummaryDto>> GetAttractionsAsync(double latitude, double longitude, double radiusKm, CancellationToken cancellationToken = default)
    {
        // F1/US3 — coordinates on the globe, 0 < radius ≤ 50 km (validator -> HTTP 400).
        await _attractionsValidator.ValidateAndThrowAppExceptionAsync(
            new GetAttractionsRequest(latitude, longitude, radiusKm), cancellationToken);

        // Coordinates rounded to ~3 decimals (≈100 m) so near-identical map
        // positions share an entry; invariant formatting so the key doesn't
        // change with the server's locale ("16.05" vs "16,05").
        var key = FormattableString.Invariant(
            $"attr:{Math.Round(latitude, 3)}:{Math.Round(longitude, 3)}:{radiusKm}");
        return await GetCachedAsync<IReadOnlyList<DestinationSummaryDto>>(
            key, AttractionsTtl, async () =>
            {
                var attractions = await _provider.GetAttractionsAsync(latitude, longitude, radiusKm, cancellationToken);

                // "Recommended" default sort (spec §11.2): rating descending, unrated
                // last — which with Geoapify (no ratings) degrades to provider order.
                // Ranked/capped BEFORE image enrichment so we only spend extra provider
                // calls on the ≤20 results we actually return.
                var ranked = attractions
                    .OrderBy(a => a.Rating is null)
                    .ThenByDescending(a => a.Rating)
                    .Take(MaxAttractionResults)
                    .ToList();

                return await EnrichWithImagesAsync(ranked, cancellationToken);
            }, cancellationToken);
    }

    /// <summary>
    /// Fills in each attraction's ImageUrl. A failed lookup for one place just
    /// leaves that one imageless — it must never take down the whole list.
    /// </summary>
    private async Task<IReadOnlyList<DestinationSummaryDto>> EnrichWithImagesAsync(
        IReadOnlyList<DestinationSummaryDto> attractions, CancellationToken cancellationToken)
    {
        var images = new ConcurrentDictionary<string, string?>();

        await Parallel.ForEachAsync(
            attractions,
            new ParallelOptions { MaxDegreeOfParallelism = ImageFetchConcurrency, CancellationToken = cancellationToken },
            async (attraction, ct) =>
            {
                images[attraction.ProviderId] = await GetAttractionImageAsync(attraction, ct);
            });

        return attractions
            .Select(a => a with { ImageUrl = images.GetValueOrDefault(a.ProviderId) })
            .ToList();
    }

    /// <summary>
    /// Serper (Google Images) first, since it finds a photo for far more places
    /// than the destination provider's own sparse image data; falls back to
    /// whatever image the caller already had on hand (the provider's own
    /// thumbnail), and finally to a stale cached gallery rather than blanking
    /// out photos that used to be there. In practice Serper rarely returns a
    /// truly empty result, even for an unrelated query, so a non-null hit is a
    /// best-effort "top hit", not a confirmed match — the later fallbacks
    /// mostly only fire when Serper itself is unreachable or times out.
    ///
    /// Shared by the attractions list (which only needs the first photo, via
    /// <see cref="GetAttractionImageAsync"/>) and the details view's carousel
    /// (up to <see cref="DetailsPhotoCount"/>) under ONE cache entry per place —
    /// otherwise the two would independently query and cache different "top
    /// hits" for the same destination, showing a different photo on the list
    /// than on its own details page.
    /// </summary>
    private async Task<IReadOnlyList<string>> GetAttractionImagesAsync(string providerId, string name, string? providerImage, CancellationToken cancellationToken)
    {
        var key = $"imgs:{providerId}";
        var stale = await TryGetCachedEnvelopeAsync<IReadOnlyList<string>>(key, cancellationToken);
        if (stale is not null && _clock.GetUtcNow() - stale.FetchedAt < ImageTtl)
        {
            return stale.Value;
        }

        IReadOnlyList<string> images = [];
        try
        {
            images = await _imageSearch.SearchImagesAsync(name, DetailsPhotoCount, cancellationToken);
        }
        catch (Exception ex) when (IsTransientExternalFailure(ex) && !cancellationToken.IsCancellationRequested)
        {
            // Serper down, timed out, or returned something unparseable — fall
            // through to the next source rather than failing the whole list/page.
        }

        if (images.Count == 0 && providerImage is not null)
        {
            images = [providerImage];
        }

        if (images.Count == 0 && stale is not null)
        {
            images = stale.Value; // nothing resolved this round — prefer a stale gallery over none at all
        }

        await SetCachedEnvelopeAsync(key, new CacheEnvelope<IReadOnlyList<string>>(images, _clock.GetUtcNow()), cancellationToken);
        return images;
    }

    /// <summary>
    /// Single-photo view over <see cref="GetAttractionImagesAsync"/> for the
    /// attractions list, which only needs a thumbnail — shares its cache so a
    /// place already looked up on the list (or its details page) isn't queried
    /// twice.
    /// </summary>
    private async Task<string?> GetAttractionImageAsync(DestinationSummaryDto attraction, CancellationToken cancellationToken)
    {
        var images = await GetAttractionImagesAsync(attraction.ProviderId, attraction.Name, attraction.ImageUrl, cancellationToken);
        return images.FirstOrDefault();
    }

    /// <summary>
    /// True for the external-call failure modes we treat as "this source
    /// didn't come through" rather than "the whole request must fail":
    /// connection failures, HttpClient timeouts (which surface as
    /// TaskCanceledException, not HttpRequestException), and a response body
    /// that doesn't deserialize as expected. Excludes a genuine caller-driven
    /// cancellation, which should propagate rather than be swallowed as
    /// "no result".
    /// </summary>
    private static bool IsTransientExternalFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or JsonException;

    /// <summary>
    /// Cache-aside fetch of one place's details, shared by <see cref="GetDetailsAsync"/>
    /// and the attractions-list image enrichment above — a place looked up during
    /// enrichment is already cached by the time the user clicks into its detail page.
    /// A null provider answer is deliberately NOT cached (checked by hand, not via
    /// <see cref="GetCachedAsync{T}"/>) so a transient "not found" doesn't stick.
    /// </summary>
    private async Task<DestinationDetailsDto?> GetCachedProviderDetailsAsync(string providerId, CancellationToken cancellationToken)
    {
        var key = $"details:{providerId}";
        var stale = await TryGetCachedEnvelopeAsync<DestinationDetailsDto>(key, cancellationToken);
        if (stale is not null && _clock.GetUtcNow() - stale.FetchedAt < DetailsTtl)
        {
            return stale.Value;
        }

        DestinationDetailsDto? details;
        try
        {
            details = await _provider.GetDestinationDetailsAsync(providerId, cancellationToken);
        }
        catch (Exception ex) when (IsTransientExternalFailure(ex) && !cancellationToken.IsCancellationRequested)
        {
            return stale?.Value; // stale-better-than-down (spec §11.2), else null
        }

        if (details is not null)
        {
            await SetCachedEnvelopeAsync(key, new CacheEnvelope<DestinationDetailsDto>(details, _clock.GetUtcNow()), cancellationToken);
        }

        return details;
    }

    public async Task<DestinationDetailsDto> GetDetailsAsync(string providerId, CancellationToken cancellationToken = default)
    {
        await _detailsValidator.ValidateAndThrowAppExceptionAsync(
            new GetDestinationDetailsRequest(providerId), cancellationToken);

        // F2/US1 (spec §11.3): the provider has the freshest data, but it can
        // say "no such place" (null) or be unreachable (falls back to stale
        // cache inside the helper) — either way, a final null falls through to
        // our own DB (DestinationService never writes; only
        // TripService.AddDestinationAsync upserts).
        var details = await GetCachedProviderDetailsAsync(providerId, cancellationToken);
        if (details is not null)
        {
            // Geoapify's own image data is sparse (see EnrichWithImagesAsync) — the
            // attractions list already papers over that with a Serper lookup, and
            // F2/US2 wants a full photo gallery here, not just one thumbnail.
            // GetAttractionImagesAsync already falls back to details.ImageUrl
            // internally when Serper finds nothing, so images.FirstOrDefault()
            // is never actually behind details.ImageUrl here.
            var images = await GetAttractionImagesAsync(details.ProviderId, details.Name, details.ImageUrl, cancellationToken);
            return details with { ImageUrl = images.FirstOrDefault(), ImageUrls = images };
        }

        // Saved-trip destinations must stay viewable even if the provider forgets
        // them (§8.4's snapshot rationale) — only a miss on BOTH sources is 404.
        var cached = await _destinations.GetByProviderIdReadOnlyAsync(providerId, cancellationToken);

        return cached?.ToDetailsDto() ?? throw new NotFoundException(nameof(Destination), providerId);
    }
}
