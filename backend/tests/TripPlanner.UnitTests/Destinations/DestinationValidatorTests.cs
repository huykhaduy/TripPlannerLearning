using FluentValidation.TestHelper;
using TripPlanner.Application.Features.Destinations.Dtos;
using TripPlanner.Application.Features.Destinations.Validators;
using Xunit;

namespace TripPlanner.UnitTests.Destinations;

/// <summary>
/// Rule-level tests for the Destination validators, concentrating on the BOUNDARIES.
/// Every limit here is a number that an off-by-one would silently move —
/// DestinationServiceTests only proves that clearly-invalid input is rejected.
/// </summary>
public class DestinationValidatorTests
{
    private static readonly SearchLocationsRequestValidator Search = new();
    private static readonly GetAttractionsRequestValidator Attractions = new();
    private static readonly GetDestinationDetailsRequestValidator Details = new();

    /// <summary>Valid coordinates, so a test can vary one field at a time.</summary>
    private static GetAttractionsRequest AttractionsRequest(
        double latitude = 48.85, double longitude = 2.35, double radiusKm = 20) =>
        new(latitude, longitude, radiusKm);

    // -----------------------------------------------------------------
    // SearchLocationsRequestValidator
    // -----------------------------------------------------------------

    [Fact]
    public void Search_WithQueryAtExactlyTheMinimumLength_IsValid()
    {
        var atMinimum = new string('a', SearchLocationsRequestValidator.MinQueryLength);

        var result = Search.TestValidate(new SearchLocationsRequest(atMinimum));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Search_WithQueryOneCharBelowTheMinimum_ReportsTheLengthMessage()
    {
        var tooShort = new string('a', SearchLocationsRequestValidator.MinQueryLength - 1);

        var result = Search.TestValidate(new SearchLocationsRequest(tooShort));

        result.ShouldHaveValidationErrorFor(x => x.Query)
            .WithErrorMessage($"Search query must be at least {SearchLocationsRequestValidator.MinQueryLength} characters.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Search_WithNoQuery_ReportsQueryRequired(string? query)
    {
        var result = Search.TestValidate(new SearchLocationsRequest(query!));

        result.ShouldHaveValidationErrorFor(x => x.Query).WithErrorMessage("Search query is required.");
    }

    // -----------------------------------------------------------------
    // GetAttractionsRequestValidator — latitude / longitude
    // -----------------------------------------------------------------

    [Theory]
    [InlineData(-90)]  // inclusive lower bound
    [InlineData(0)]
    [InlineData(90)]   // inclusive upper bound
    public void Attractions_WithLatitudeOnOrInsideTheBounds_IsValid(double latitude)
    {
        var result = Attractions.TestValidate(AttractionsRequest(latitude: latitude));

        result.ShouldNotHaveValidationErrorFor(x => x.Latitude);
    }

    [Theory]
    [InlineData(-90.1)]
    [InlineData(90.1)]
    public void Attractions_WithLatitudeOutsideTheBounds_ReportsLatitude(double latitude)
    {
        var result = Attractions.TestValidate(AttractionsRequest(latitude: latitude));

        result.ShouldHaveValidationErrorFor(x => x.Latitude)
            .WithErrorMessage("Latitude must be between -90 and 90.");
    }

    [Theory]
    [InlineData(-180)] // inclusive lower bound
    [InlineData(0)]
    [InlineData(180)]  // inclusive upper bound
    public void Attractions_WithLongitudeOnOrInsideTheBounds_IsValid(double longitude)
    {
        var result = Attractions.TestValidate(AttractionsRequest(longitude: longitude));

        result.ShouldNotHaveValidationErrorFor(x => x.Longitude);
    }

    [Theory]
    [InlineData(-180.1)]
    [InlineData(180.1)]
    public void Attractions_WithLongitudeOutsideTheBounds_ReportsLongitude(double longitude)
    {
        var result = Attractions.TestValidate(AttractionsRequest(longitude: longitude));

        result.ShouldHaveValidationErrorFor(x => x.Longitude)
            .WithErrorMessage("Longitude must be between -180 and 180.");
    }

    // -----------------------------------------------------------------
    // GetAttractionsRequestValidator — radius
    // -----------------------------------------------------------------

    [Fact]
    public void Attractions_WithRadiusAtExactlyTheMaximum_IsValid()
    {
        // LessThanOrEqualTo, so the maximum itself must be accepted.
        var result = Attractions.TestValidate(
            AttractionsRequest(radiusKm: GetAttractionsRequestValidator.MaxRadiusKm));

        result.ShouldNotHaveValidationErrorFor(x => x.RadiusKm);
    }

    [Fact]
    public void Attractions_WithRadiusJustAboveTheMaximum_ReportsRadius()
    {
        var result = Attractions.TestValidate(
            AttractionsRequest(radiusKm: GetAttractionsRequestValidator.MaxRadiusKm + 0.1));

        result.ShouldHaveValidationErrorFor(x => x.RadiusKm)
            .WithErrorMessage($"Radius must be at most {GetAttractionsRequestValidator.MaxRadiusKm} km.");
    }

    [Theory]
    [InlineData(0)]   // GreaterThan(0), so zero is NOT allowed
    [InlineData(-1)]
    public void Attractions_WithNonPositiveRadius_ReportsRadius(double radiusKm)
    {
        var result = Attractions.TestValidate(AttractionsRequest(radiusKm: radiusKm));

        result.ShouldHaveValidationErrorFor(x => x.RadiusKm)
            .WithErrorMessage("Radius must be greater than 0 km.");
    }

    [Fact]
    public void Attractions_WithATinyPositiveRadius_IsValid()
    {
        var result = Attractions.TestValidate(AttractionsRequest(radiusKm: 0.1));

        result.ShouldNotHaveValidationErrorFor(x => x.RadiusKm);
    }

    // -----------------------------------------------------------------
    // GetDestinationDetailsRequestValidator
    // -----------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Details_WithoutAProviderId_ReportsIdRequired(string? providerId)
    {
        var result = Details.TestValidate(new GetDestinationDetailsRequest(providerId!));

        result.ShouldHaveValidationErrorFor(x => x.ProviderId)
            .WithErrorMessage("A destination id is required.");
    }

    [Fact]
    public void Details_WithAProviderId_IsValid()
    {
        var result = Details.TestValidate(new GetDestinationDetailsRequest("geo-123"));

        result.ShouldNotHaveAnyValidationErrors();
    }
}
