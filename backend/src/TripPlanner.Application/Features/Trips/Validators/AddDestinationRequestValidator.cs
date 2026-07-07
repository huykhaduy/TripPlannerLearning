using FluentValidation;
using TripPlanner.Application.Features.Trips.Dtos;

namespace TripPlanner.Application.Features.Trips.Validators;

/// <summary>
/// F3/US3 — the external provider id is required. Whether the optional
/// ItineraryDayId belongs to the trip is checked in the service (needs the DB).
/// </summary>
public class AddDestinationRequestValidator : AbstractValidator<AddDestinationRequest>
{
    public AddDestinationRequestValidator()
    {
        RuleFor(x => x.ProviderId)
            .NotEmpty().WithMessage("A destination provider id is required.");
    }
}
