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

        return new DestinationService(provider.Object, new SearchLocationsRequestValidator());
    }

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
        var sut = new DestinationService(provider.Object, new SearchLocationsRequestValidator());

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
}
