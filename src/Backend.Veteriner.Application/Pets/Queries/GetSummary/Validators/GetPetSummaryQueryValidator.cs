using FluentValidation;

namespace Backend.Veteriner.Application.Pets.Queries.GetSummary.Validators;

public sealed class GetPetSummaryQueryValidator : AbstractValidator<GetPetSummaryQuery>
{
    public GetPetSummaryQueryValidator()
    {
        RuleFor(x => x.PetId).NotEmpty();
    }
}
