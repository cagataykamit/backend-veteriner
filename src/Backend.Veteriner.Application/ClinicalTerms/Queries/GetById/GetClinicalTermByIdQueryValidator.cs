using FluentValidation;

namespace Backend.Veteriner.Application.ClinicalTerms.Queries.GetById;

public sealed class GetClinicalTermByIdQueryValidator : AbstractValidator<GetClinicalTermByIdQuery>
{
    public GetClinicalTermByIdQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
