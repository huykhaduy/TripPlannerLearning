using FluentValidation.TestHelper;
using TripPlanner.Application.Features.Trips.Dtos;
using TripPlanner.Application.Features.Trips.Validators;
using Xunit;

namespace TripPlanner.UnitTests.Trips;

/// <summary>
/// Rule-level tests for the Trip validators. The trip-length rule is the reason this
/// file matters: it is written as a strict "&lt; MaxTripLengthDays" against a DIFFERENCE
/// of day numbers, so its real boundary is one day away from where the message reads,
/// and nothing else in the suite pins it.
/// </summary>
public class TripValidatorTests
{
    private static readonly CreateTripRequestValidator CreateTrip = new();
    private static readonly UpdateTripRequestValidator UpdateTrip = new();
    private static readonly AddDestinationRequestValidator AddDestination = new();
    private static readonly UpdateItineraryItemRequestValidator UpdateItem = new();

    private static readonly DateOnly Start = new(2026, 1, 1);

    // -----------------------------------------------------------------
    // CreateTripRequestValidator
    // -----------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateTrip_WithoutAName_ReportsNameRequired(string? name)
    {
        var result = CreateTrip.TestValidate(new CreateTripRequest(name!));

        result.ShouldHaveValidationErrorFor(x => x.Name).WithErrorMessage("Trip name is required.");
    }

    [Fact]
    public void CreateTrip_WithNameAtExactlyTheMaximumLength_IsValid()
    {
        var atMaximum = new string('n', CreateTripRequestValidator.MaxNameLength);

        var result = CreateTrip.TestValidate(new CreateTripRequest(atMaximum));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void CreateTrip_WithNameOneCharOverTheMaximum_ReportsName()
    {
        var tooLong = new string('n', CreateTripRequestValidator.MaxNameLength + 1);

        var result = CreateTrip.TestValidate(new CreateTripRequest(tooLong));

        // No custom message on this rule — FluentValidation's default is used, so only
        // the property is asserted.
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    // -----------------------------------------------------------------
    // UpdateTripRequestValidator — name
    // -----------------------------------------------------------------

    [Fact]
    public void UpdateTrip_WithoutAName_ReportsNameRequired()
    {
        var result = UpdateTrip.TestValidate(new UpdateTripRequest("", Start, Start.AddDays(3)));

        result.ShouldHaveValidationErrorFor(x => x.Name).WithErrorMessage("Trip name is required.");
    }

    [Fact]
    public void UpdateTrip_SharesTheNameLengthLimitWithCreate()
    {
        var tooLong = new string('n', CreateTripRequestValidator.MaxNameLength + 1);

        var result = UpdateTrip.TestValidate(new UpdateTripRequest(tooLong, null, null));

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    // -----------------------------------------------------------------
    // UpdateTripRequestValidator — trip length
    // -----------------------------------------------------------------

    /// <summary>
    /// The rule is <c>end.DayNumber - start.DayNumber &lt; 365</c>, so the largest
    /// accepted difference is 364 — which is a 365-day trip counted inclusively, and
    /// matches what the message promises. Loosening the comparison to &lt;= would
    /// silently allow 366-day trips.
    /// </summary>
    [Fact]
    public void UpdateTrip_WithTheLongestAllowedRange_IsValid()
    {
        var end = Start.AddDays(UpdateTripRequestValidator.MaxTripLengthDays - 1);

        var result = UpdateTrip.TestValidate(new UpdateTripRequest("Long trip", Start, end));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void UpdateTrip_WithARangeOneDayTooLong_ReportsEndDate()
    {
        var end = Start.AddDays(UpdateTripRequestValidator.MaxTripLengthDays);

        var result = UpdateTrip.TestValidate(new UpdateTripRequest("Too long", Start, end));

        result.ShouldHaveValidationErrorFor(x => x.EndDate)
            .WithErrorMessage($"A trip cannot be longer than {UpdateTripRequestValidator.MaxTripLengthDays} days.");
    }

    [Theory]
    [InlineData(true, false)]   // start set, end missing
    [InlineData(false, true)]   // end set, start missing
    [InlineData(false, false)]  // neither set — an undated trip
    public void UpdateTrip_WithAnIncompleteDateRange_SkipsTheLengthRule(bool hasStart, bool hasEnd)
    {
        // A half-open range has no length to check; F3/US2 also allows a trip with no
        // dates at all, which is what an itinerary starts as.
        var result = UpdateTrip.TestValidate(new UpdateTripRequest(
            "Trip",
            hasStart ? Start : null,
            hasEnd ? Start.AddDays(3) : null));

        result.ShouldNotHaveValidationErrorFor(x => x.EndDate);
    }

    [Fact]
    public void UpdateTrip_WithEndBeforeStart_IsNotTheValidatorsJob()
    {
        // start <= end is a DOMAIN rule enforced by Trip.SetDates (DomainException ->
        // 400), deliberately not duplicated here. If this ever starts failing, the
        // rule has been added in two places.
        var result = UpdateTrip.TestValidate(new UpdateTripRequest("Backwards", Start, Start.AddDays(-5)));

        result.ShouldNotHaveAnyValidationErrors();
    }

    // -----------------------------------------------------------------
    // AddDestinationRequestValidator
    // -----------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddDestination_WithoutAProviderId_ReportsIdRequired(string? providerId)
    {
        var result = AddDestination.TestValidate(new AddDestinationRequest(providerId!, null));

        result.ShouldHaveValidationErrorFor(x => x.ProviderId)
            .WithErrorMessage("A destination provider id is required.");
    }

    [Fact]
    public void AddDestination_WithNoItineraryDay_IsValid()
    {
        // A null day means Saved Places, not a missing value. Whether a non-null day
        // belongs to the trip is checked by TripService, which needs the database.
        var result = AddDestination.TestValidate(new AddDestinationRequest("geo-123", null));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void AddDestination_WithAnItineraryDay_IsValid()
    {
        var result = AddDestination.TestValidate(new AddDestinationRequest("geo-123", Guid.NewGuid()));

        result.ShouldNotHaveAnyValidationErrors();
    }

    // -----------------------------------------------------------------
    // UpdateItineraryItemRequestValidator
    // -----------------------------------------------------------------

    [Fact]
    public void UpdateItem_WithPositionZero_IsValid()
    {
        var result = UpdateItem.TestValidate(new UpdateItineraryItemRequest(null, 0));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void UpdateItem_WithANegativePosition_ReportsSortOrder()
    {
        var result = UpdateItem.TestValidate(new UpdateItineraryItemRequest(null, -1));

        result.ShouldHaveValidationErrorFor(x => x.SortOrder)
            .WithErrorMessage("The position must be zero or greater.");
    }

    [Fact]
    public void UpdateItem_WithAPositionPastTheEndOfTheBucket_IsValid()
    {
        // There is no upper bound on purpose — TripService clamps the position to the
        // bucket size, so "99" simply means "last".
        var result = UpdateItem.TestValidate(new UpdateItineraryItemRequest(null, 99));

        result.ShouldNotHaveAnyValidationErrors();
    }
}
