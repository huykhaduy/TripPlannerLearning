using FluentValidation;
using TripPlanner.Application.Features.Destinations.Dtos;

namespace TripPlanner.Application.Features.Destinations.Validators;

/// <summary>F1/US3 — coordinates must be on the globe, radius within spec §11.2's cap.</summary>
public class GetAttractionsRequestValidator : AbstractValidator<GetAttractionsRequest>
{
    public const double MaxRadiusKm = 50;

    public GetAttractionsRequestValidator()
    {
        RuleFor(x => x.Latitude)
            .InclusiveBetween(-90, 90).WithMessage("Latitude must be between -90 and 90.");

        RuleFor(x => x.Longitude)
            .InclusiveBetween(-180, 180).WithMessage("Longitude must be between -180 and 180.");

        RuleFor(x => x.RadiusKm)
            .GreaterThan(0).WithMessage("Radius must be greater than 0 km.")
            .LessThanOrEqualTo(MaxRadiusKm).WithMessage($"Radius must be at most {MaxRadiusKm} km.");
    }
}
