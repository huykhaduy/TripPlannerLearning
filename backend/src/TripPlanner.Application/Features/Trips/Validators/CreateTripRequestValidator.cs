using FluentValidation;
using TripPlanner.Application.Features.Trips.Dtos;

namespace TripPlanner.Application.Features.Trips.Validators;

/// <summary>F3/US1 — a trip must have a (non-blank) name.</summary>
public class CreateTripRequestValidator : AbstractValidator<CreateTripRequest>
{
    public const int MaxNameLength = 100;

    public CreateTripRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Trip name is required.")
            .MaximumLength(MaxNameLength);
    }
}
