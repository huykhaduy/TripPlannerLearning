using Moq;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Application.Features.Destinations;
using TripPlanner.Application.Features.Destinations.Dtos;
using TripPlanner.Application.Features.Destinations.Validators;
using Xunit;

namespace TripPlanner.Application.Tests.Destinations;

/// <summary>
/// Unit tests for the search half of Feature 1 (US1/US2). No database needed:
/// the service's dependencies are just the provider (mocked — we never hit the
/// real Geoapify API in tests) and the validator (real — it's pure logic).
/// </summary>
public class DestinationServiceTests
{
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

        return CreateSut(provider);
    }

    /// <summary>As above, but the provider returns attractions (F1/US3 tests).</summary>
    private static DestinationService CreateAttractionsSut(params DestinationSummaryDto[] providerResults)
    {
        var provider = new Mock<IDestinationProvider>();
        provider
            .Setup(p => p.GetAttractionsAsync(
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(providerResults);

        return CreateSut(provider);
    }

    private static DestinationService CreateSut(Mock<IDestinationProvider> provider) =>
        new(provider.Object, new SearchLocationsRequestValidator(), new GetAttractionsRequestValidator());

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
        var sut = CreateSut(provider);

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
}
