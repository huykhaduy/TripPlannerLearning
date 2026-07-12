using FluentValidation;
using TripPlanner.Application.Features.Trips.Dtos;

namespace TripPlanner.Application.Features.Trips.Validators;

/// <summary>
/// F3/US4-US6 — the desired position must be non-negative. Whether the optional
/// ItineraryDayId belongs to the trip is checked in the service (needs the DB).
/// </summary>
public class UpdateItineraryItemRequestValidator : AbstractValidator<UpdateItineraryItemRequest>
{
    public UpdateItineraryItemRequestValidator()
    {
        RuleFor(x => x.SortOrder)
            .GreaterThanOrEqualTo(0).WithMessage("The position must be zero or greater.");
    }
}
