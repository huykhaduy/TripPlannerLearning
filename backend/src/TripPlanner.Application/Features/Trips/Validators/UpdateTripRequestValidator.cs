using FluentValidation;
using TripPlanner.Application.Features.Trips.Dtos;

namespace TripPlanner.Application.Features.Trips.Validators;

/// <summary>
/// F3/US2 — name stays required on rename; absurd date ranges are rejected
/// (spec §11.1: max 365 days). start ≤ end is NOT checked here — that rule
/// belongs to the domain (<c>Trip.SetDates</c>) and must not be duplicated.
/// </summary>
public class UpdateTripRequestValidator : AbstractValidator<UpdateTripRequest>
{
    public const int MaxTripLengthDays = 365;

    public UpdateTripRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Trip name is required.")
            .MaximumLength(CreateTripRequestValidator.MaxNameLength);

        RuleFor(x => x.EndDate)
            .Must((request, endDate) =>
                request.StartDate is null || endDate is null ||
                endDate.Value.DayNumber - request.StartDate.Value.DayNumber < MaxTripLengthDays)
            .WithMessage($"A trip cannot be longer than {MaxTripLengthDays} days.");
    }
}
