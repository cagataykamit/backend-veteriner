using Backend.Veteriner.Domain.Catalog;
using FluentValidation;

namespace Backend.Veteriner.Application.ClinicalSnippets.Queries.GetList;

public sealed class GetClinicalSnippetsListQueryValidator : AbstractValidator<GetClinicalSnippetsListQuery>
{
    public GetClinicalSnippetsListQueryValidator()
    {
        RuleFor(x => x.PageRequest).NotNull();
        RuleFor(x => x.PageRequest.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageRequest.PageSize).InclusiveBetween(1, ClinicalSnippet.MaxPerTenant);
    }
}
