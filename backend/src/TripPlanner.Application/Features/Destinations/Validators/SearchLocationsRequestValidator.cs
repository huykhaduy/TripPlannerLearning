using FluentValidation;
using TripPlanner.Application.Features.Destinations.Dtos;

namespace TripPlanner.Application.Features.Destinations.Validators;

/// <summary>F1/US1 &amp; US2 — a search needs at least 2 characters before we call the provider.</summary>
public class SearchLocationsRequestValidator : AbstractValidator<SearchLocationsRequest>
{
    public const int MinQueryLength = 2;

    public SearchLocationsRequestValidator()
    {
        RuleFor(x => x.Query)
            .NotEmpty().WithMessage("Search query is required.")
            .MinimumLength(MinQueryLength)
            .WithMessage($"Search query must be at least {MinQueryLength} characters.");
    }
}
