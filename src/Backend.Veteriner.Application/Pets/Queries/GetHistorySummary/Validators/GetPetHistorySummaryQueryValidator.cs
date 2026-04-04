using Backend.Veteriner.Application.Pets.Queries.GetHistorySummary;
using FluentValidation;

namespace Backend.Veteriner.Application.Pets.Queries.GetHistorySummary.Validators;

public sealed class GetPetHistorySummaryQueryValidator : AbstractValidator<GetPetHistorySummaryQuery>
{
    public GetPetHistorySummaryQueryValidator()
    {
        RuleFor(x => x.PetId).NotEmpty();
    }
}
