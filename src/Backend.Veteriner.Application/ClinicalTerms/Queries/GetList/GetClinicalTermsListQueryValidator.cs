using FluentValidation;

namespace Backend.Veteriner.Application.ClinicalTerms.Queries.GetList;

public sealed class GetClinicalTermsListQueryValidator : AbstractValidator<GetClinicalTermsListQuery>
{
    public GetClinicalTermsListQueryValidator()
    {
        RuleFor(x => x.PageRequest).NotNull();
        RuleFor(x => x.PageRequest.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageRequest.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.Kind).IsInEnum().When(x => x.Kind.HasValue);
    }
}
