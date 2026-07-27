using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Application.Features.Destinations;
using TripPlanner.Application.Features.Destinations.Dtos;
using TripPlanner.Application.Features.Destinations.Validators;
using TripPlanner.Infrastructure.Persistence;
using TripPlanner.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TripPlanner.Application.Tests.Destinations;

/// <summary>
/// Unit tests for Feature 1 (US1-3 search/attractions) and Feature 2 (US1
/// details). The service's dependencies are the provider and image search
/// (mocked — we never hit the real Geoapify/Serper APIs in tests), the
/// validators (real — pure logic), and for GetDetailsAsync's DB fallback, an
/// in-memory EF Core database.
/// </summary>
public class DestinationServiceTests
{
    private static ApplicationDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);

    /// <summary>
    /// Builds the service with a mocked provider that returns
    /// <paramref name="providerResults"/> for ANY query.
    /// </summary>
    private static DestinationService CreateSut(params LocationSuggestionDto[] providerResults)
    {
        var provider = new Mock<IDestinationProvider>();
        provider
            .Setup(p => p.SearchLocationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(providerResults);

        return CreateSut(CreateDb(), provider);
    }

    /// <summary>As above, but the provider returns attractions (F1/US3 tests).</summary>
    private static DestinationService CreateAttractionsSut(params DestinationSummaryDto[] providerResults)
    {
        var provider = new Mock<IDestinationProvider>();
        provider
            .Setup(p => p.GetAttractionsAsync(
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(providerResults);

        return CreateSut(CreateDb(), provider);
    }

    /// <summary>
    /// Controllable wall clock: caching TTLs are checked against this, so a
    /// test can "wait 25 hours" by moving <see cref="Now"/> forward.
    /// </summary>
    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>An image provider that never finds anything, by default — tests that
    /// care about ImageUrl pass their own mock instead.</summary>
    private static Mock<IImageSearchProvider> NoOpImageSearch()
    {
        var imageSearch = new Mock<IImageSearchProvider>();
        imageSearch
            .Setup(p => p.SearchImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        imageSearch
            .Setup(p => p.SearchImagesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string>)[]);
        return imageSearch;
    }

    private static DestinationService CreateSut(
        ApplicationDbContext db,
        Mock<IDestinationProvider> provider,
        FakeClock? clock = null,
        Mock<IImageSearchProvider>? imageSearch = null) =>
        new(new DestinationRepository(db), provider.Object, (imageSearch ?? NoOpImageSearch()).Object,
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), clock ?? new FakeClock(),
            new SearchLocationsRequestValidator(), new GetAttractionsRequestValidator(),
            new GetDestinationDetailsRequestValidator());

    /// <summary>Shorthand — coordinates don't matter for these tests.</summary>
    private static LocationSuggestionDto Suggestion(string name, string? country = null) =>
        new(name, country, Latitude: 0, Longitude: 0);

    [Fact]
    public async Task SearchLocationsAsync_RanksExactThenPrefixThenSubstring_AndDedupes()
    {
        // Provider order is deliberately "wrong" (substring hit first, duplicate
        // in the middle) so the test proves the service re-ranks and dedupes.
        var sut = CreateSut(
            Suggestion("Gay Paris", "France"),        // substring match  -> rank 2
            Suggestion("Parisot", "France"),          // prefix match     -> rank 1
            Suggestion("PARISOT", "France"),          // duplicate of ^ (case-insensitive)
            Suggestion("Paris", "United States"),     // exact match      -> rank 0
            Suggestion("Paris", "France"));           // exact match      -> rank 0

        var results = await sut.SearchLocationsAsync("paris");

        Assert.Equal(4, results.Count); // duplicate collapsed
        // Exact matches first (provider order kept between equals), then prefix, then substring.
        Assert.Equal(["Paris", "Paris", "Parisot", "Gay Paris"], results.Select(r => r.Name));
        Assert.Equal("United States", results[0].Country);
        Assert.Equal("France", results[1].Country);
    }

    [Fact]
    public async Task SearchLocationsAsync_WithMoreThanFiveResults_CapsAtFive()
    {
        var sut = CreateSut(
            Suggestion("Springfield", "United States"),
            Suggestion("Springfield", "Canada"),
            Suggestion("Springfield", "Australia"),
            Suggestion("Springfield", "New Zealand"),
            Suggestion("Springfield", "Ireland"),
            Suggestion("Springfield", "South Africa"),
            Suggestion("Springfield", "United Kingdom"));

        var results = await sut.SearchLocationsAsync("springfield");

        Assert.Equal(5, results.Count);
    }

    [Fact]
    public async Task SearchLocationsAsync_WithShortQuery_ThrowsValidation()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ValidationException>(() => sut.SearchLocationsAsync("a"));
    }

    [Fact]
    public async Task SearchLocationsAsync_WithWhitespacePaddedShortQuery_ThrowsValidation()
    {
        // 3 raw characters, but only 1 after trimming — the trimmed length is
        // what the spec validates.
        var sut = CreateSut();

        await Assert.ThrowsAsync<ValidationException>(() => sut.SearchLocationsAsync(" a "));
    }

    [Fact]
    public async Task SearchLocationsAsync_TrimsQueryBeforeCallingProvider()
    {
        // Built inline instead of via CreateSut: this test asserts on the mock
        // itself (HOW the provider was called), so it needs to keep a reference.
        var provider = new Mock<IDestinationProvider>();
        provider
            .Setup(p => p.SearchLocationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var sut = CreateSut(CreateDb(), provider);

        await sut.SearchLocationsAsync("  paris  ");

        provider.Verify(
            p => p.SearchLocationsAsync("paris", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchLocationsAsync_WhenProviderReturnsNothing_ReturnsEmptyList()
    {
        // "No matches" is a successful answer (200 + []), never an error.
        var sut = CreateSut();

        var results = await sut.SearchLocationsAsync("zzzzqqq");

        Assert.Empty(results);
    }

    // ------------------------------------------------------------------
    // F1/US3 — GetAttractionsAsync
    // ------------------------------------------------------------------

    /// <summary>Shorthand — only the fields under test vary.</summary>
    private static DestinationSummaryDto Attraction(string name, double? rating = null) =>
        new(ProviderId: $"id-{name}", Name: name, Category: null, ImageUrl: null, Rating: rating);

    [Fact]
    public async Task GetAttractionsAsync_OrdersByRatingDescending_UnratedLast()
    {
        var sut = CreateAttractionsSut(
            Attraction("No rating A"),
            Attraction("Three stars", rating: 3.0),
            Attraction("Five stars", rating: 5.0),
            Attraction("No rating B"));

        var results = await sut.GetAttractionsAsync(latitude: 48.85, longitude: 2.35, radiusKm: 20);

        Assert.Equal(
            ["Five stars", "Three stars", "No rating A", "No rating B"],
            results.Select(r => r.Name));
    }

    [Fact]
    public async Task GetAttractionsAsync_WithMoreThanTwentyResults_CapsAtTwenty()
    {
        var many = Enumerable.Range(1, 25).Select(i => Attraction($"POI {i}")).ToArray();
        var sut = CreateAttractionsSut(many);

        var results = await sut.GetAttractionsAsync(latitude: 0, longitude: 0, radiusKm: 20);

        Assert.Equal(20, results.Count);
    }

    [Theory]
    [InlineData(91, 0, 20)]     // latitude off the globe
    [InlineData(-91, 0, 20)]
    [InlineData(0, 181, 20)]    // longitude off the globe
    [InlineData(0, 0, 0)]       // zero radius
    [InlineData(0, 0, 51)]      // radius beyond the 50 km cap
    public async Task GetAttractionsAsync_WithInvalidInputs_ThrowsValidation(
        double latitude, double longitude, double radiusKm)
    {
        var sut = CreateAttractionsSut();

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.GetAttractionsAsync(latitude, longitude, radiusKm));
    }

    // ------------------------------------------------------------------
    // F2/US1 — GetDetailsAsync
    // ------------------------------------------------------------------

    private static Mock<IDestinationProvider> ProviderReturning(DestinationDetailsDto? details)
    {
        var provider = new Mock<IDestinationProvider>();
        provider
            .Setup(p => p.GetDestinationDetailsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(details);
        return provider;
    }

    private static Mock<IDestinationProvider> ProviderThatIsDown()
    {
        var provider = new Mock<IDestinationProvider>();
        provider
            .Setup(p => p.GetDestinationDetailsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("simulated outage"));
        return provider;
    }

    [Fact]
    public async Task GetDetailsAsync_WhenProviderHasTheDestination_ReturnsProviderData()
    {
        var details = new DestinationDetailsDto(
            "geo-1", "Golden Bridge", "tourism", "A hand-shaped bridge.", "img.jpg",
            15.9, 108.0, "Da Nang", "https://example.com", "9am-5pm");
        var sut = CreateSut(CreateDb(), ProviderReturning(details));

        var result = await sut.GetDetailsAsync("geo-1");

        Assert.Equal("Golden Bridge", result.Name);
        Assert.Equal("A hand-shaped bridge.", result.Description);
    }

    [Fact]
    public async Task GetDetailsAsync_UsesImageSearchProvider_ForPhotoGallery()
    {
        var details = new DestinationDetailsDto(
            "geo-1", "Golden Bridge", "tourism", "A hand-shaped bridge.", "provider.jpg",
            15.9, 108.0, "Da Nang", "https://example.com", "9am-5pm");
        var imageSearch = new Mock<IImageSearchProvider>();
        imageSearch
            .Setup(p => p.SearchImagesAsync("Golden Bridge", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string>)["a.jpg", "b.jpg", "c.jpg"]);
        var sut = CreateSut(CreateDb(), ProviderReturning(details), imageSearch: imageSearch);

        var result = await sut.GetDetailsAsync("geo-1");

        Assert.Equal(["a.jpg", "b.jpg", "c.jpg"], result.ImageUrls);
        Assert.Equal("a.jpg", result.ImageUrl); // primary photo mirrors the gallery's first image
    }

    [Fact]
    public async Task GetDetailsAsync_WhenImageSearchFindsNothing_FallsBackToProviderImage()
    {
        var details = new DestinationDetailsDto(
            "geo-1", "Golden Bridge", null, null, "provider.jpg", null, null, null, null, null);
        var sut = CreateSut(CreateDb(), ProviderReturning(details)); // NoOpImageSearch -> empty gallery

        var result = await sut.GetDetailsAsync("geo-1");

        Assert.Equal(["provider.jpg"], result.ImageUrls);
        Assert.Equal("provider.jpg", result.ImageUrl);
    }

    [Fact]
    public async Task GetAttractionsAsync_ThenGetDetailsAsync_ShareOneImageCacheEntry()
    {
        // Regression test: the list and the details view used to query and cache
        // Serper independently (img:{id} vs imgs:{id}), so the same destination
        // could show a different "top hit" photo on each. They now share one
        // cache entry, so both must agree — and Serper is only queried once.
        var provider = new Mock<IDestinationProvider>();
        provider
            .Setup(p => p.GetAttractionsAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DestinationSummaryDto("geo-1", "Golden Bridge", null, null, null)]);
        provider
            .Setup(p => p.GetDestinationDetailsAsync("geo-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DestinationDetailsDto(
                "geo-1", "Golden Bridge", null, null, null, null, null, null, null, null));

        var imageSearch = new Mock<IImageSearchProvider>();
        imageSearch
            .Setup(p => p.SearchImagesAsync("Golden Bridge", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string>)["a.jpg", "b.jpg"]);
        var sut = CreateSut(CreateDb(), provider, imageSearch: imageSearch);

        var listResult = await sut.GetAttractionsAsync(latitude: 0, longitude: 0, radiusKm: 20);
        var detailsResult = await sut.GetDetailsAsync("geo-1");

        Assert.Equal("a.jpg", Assert.Single(listResult).ImageUrl);
        Assert.Equal("a.jpg", detailsResult.ImageUrl);
        Assert.Equal(["a.jpg", "b.jpg"], detailsResult.ImageUrls);
        imageSearch.Verify(
            p => p.SearchImagesAsync("Golden Bridge", It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetDetailsAsync_WhenProviderReturnsNull_FallsBackToCachedRow()
    {
        using var db = CreateDb();
        db.Destinations.Add(new Domain.Entities.Destination
        {
            ProviderId = "geo-1", Name = "Golden Bridge (cached)", Category = "tourism",
        });
        await db.SaveChangesAsync();
        var sut = CreateSut(db, ProviderReturning(null));

        var result = await sut.GetDetailsAsync("geo-1");

        Assert.Equal("Golden Bridge (cached)", result.Name);
    }

    [Fact]
    public async Task GetDetailsAsync_WhenProviderIsDown_FallsBackToCachedRow()
    {
        using var db = CreateDb();
        db.Destinations.Add(new Domain.Entities.Destination { ProviderId = "geo-1", Name = "Golden Bridge (cached)" });
        await db.SaveChangesAsync();
        var sut = CreateSut(db, ProviderThatIsDown());

        var result = await sut.GetDetailsAsync("geo-1");

        Assert.Equal("Golden Bridge (cached)", result.Name);
    }

    [Fact]
    public async Task GetDetailsAsync_WhenBothProviderAndCacheMiss_ThrowsNotFound()
    {
        var sut = CreateSut(CreateDb(), ProviderReturning(null));

        await Assert.ThrowsAsync<NotFoundException>(() => sut.GetDetailsAsync("unknown-id"));
    }

    [Fact]
    public async Task GetDetailsAsync_NeverPersistsToTheCache()
    {
        // DestinationService (search/details) never writes rows — only
        // TripService.AddDestinationAsync upserts, on first add to a trip.
        using var db = CreateDb();
        var details = new DestinationDetailsDto("geo-1", "Golden Bridge", null, null, null, null, null, null, null, null);
        var sut = CreateSut(db, ProviderReturning(details));

        await sut.GetDetailsAsync("geo-1");

        Assert.Equal(0, await db.Destinations.CountAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetDetailsAsync_WithBlankProviderId_ThrowsValidation(string providerId)
    {
        var sut = CreateSut(CreateDb(), ProviderReturning(null));

        await Assert.ThrowsAsync<ValidationException>(() => sut.GetDetailsAsync(providerId));
    }

    // ------------------------------------------------------------------
    // Caching (NFR1/NFR2) + stale-better-than-down (spec §11.2)
    // ------------------------------------------------------------------

    [Fact]
    public async Task SearchLocationsAsync_RepeatedQuery_CallsProviderOnce()
    {
        var provider = new Mock<IDestinationProvider>();
        provider
            .Setup(p => p.SearchLocationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Suggestion("Paris", "France")]);
        var sut = CreateSut(CreateDb(), provider);

        await sut.SearchLocationsAsync("paris");
        // Different casing and padding, same cache key after trim + lower.
        var second = await sut.SearchLocationsAsync("  PARIS ");

        Assert.Equal("Paris", Assert.Single(second).Name);
        provider.Verify(
            p => p.SearchLocationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchLocationsAsync_DifferentQueries_CallProviderEachTime()
    {
        var provider = new Mock<IDestinationProvider>();
        provider
            .Setup(p => p.SearchLocationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var sut = CreateSut(CreateDb(), provider);

        await sut.SearchLocationsAsync("paris");
        await sut.SearchLocationsAsync("hanoi");

        provider.Verify(
            p => p.SearchLocationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task GetAttractionsAsync_SameCoordinates_CallsProviderOnce()
    {
        var provider = new Mock<IDestinationProvider>();
        provider
            .Setup(p => p.GetAttractionsAsync(
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Attraction("Golden Bridge")]);
        var sut = CreateSut(CreateDb(), provider);

        await sut.GetAttractionsAsync(16.0545, 108.2022, 20);
        var second = await sut.GetAttractionsAsync(16.0545, 108.2022, 20);

        Assert.Equal("Golden Bridge", Assert.Single(second).Name);
        provider.Verify(
            p => p.GetAttractionsAsync(
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetDetailsAsync_RepeatedProviderId_CallsProviderOnce()
    {
        var details = new DestinationDetailsDto(
            "geo-1", "Golden Bridge", null, null, null, null, null, null, null, null);
        var provider = ProviderReturning(details);
        var sut = CreateSut(CreateDb(), provider);

        await sut.GetDetailsAsync("geo-1");
        var second = await sut.GetDetailsAsync("geo-1");

        Assert.Equal("Golden Bridge", second.Name);
        provider.Verify(
            p => p.GetDestinationDetailsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchLocationsAsync_ExpiredEntryAndProviderDown_ServesStaleResult()
    {
        var clock = new FakeClock();
        var provider = new Mock<IDestinationProvider>();
        provider
            .SetupSequence(p => p.SearchLocationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Suggestion("Paris", "France")])
            .ThrowsAsync(new HttpRequestException("simulated outage"));
        var sut = CreateSut(CreateDb(), provider, clock);

        await sut.SearchLocationsAsync("paris");
        clock.Now += TimeSpan.FromHours(25); // past the 24 h TTL -> refetch -> provider is down

        var result = await sut.SearchLocationsAsync("paris");

        Assert.Equal("Paris", Assert.Single(result).Name); // stale entry served, no 500
    }

    [Fact]
    public async Task GetDetailsAsync_ExpiredEntryAndProviderDown_ServesStaleResult()
    {
        var clock = new FakeClock();
        var details = new DestinationDetailsDto(
            "geo-1", "Golden Bridge", null, null, null, null, null, null, null, null);
        var provider = new Mock<IDestinationProvider>();
        provider
            .SetupSequence(p => p.GetDestinationDetailsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(details)
            .ThrowsAsync(new HttpRequestException("simulated outage"));
        // Empty DB: if the stale entry were not served, this would be a 404.
        var sut = CreateSut(CreateDb(), provider, clock);

        await sut.GetDetailsAsync("geo-1");
        clock.Now += TimeSpan.FromHours(25);

        var result = await sut.GetDetailsAsync("geo-1");

        Assert.Equal("Golden Bridge", result.Name);
    }
}
