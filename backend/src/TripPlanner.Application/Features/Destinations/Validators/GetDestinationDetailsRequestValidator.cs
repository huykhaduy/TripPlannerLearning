using FluentValidation;
using TripPlanner.Application.Features.Destinations.Dtos;

namespace TripPlanner.Application.Features.Destinations.Validators;

/// <summary>F2/US1 — a details lookup needs a non-blank provider id.</summary>
public class GetDestinationDetailsRequestValidator : AbstractValidator<GetDestinationDetailsRequest>
{
    public GetDestinationDetailsRequestValidator()
    {
        RuleFor(x => x.ProviderId)
            .NotEmpty().WithMessage("A destination id is required.");
    }
}
