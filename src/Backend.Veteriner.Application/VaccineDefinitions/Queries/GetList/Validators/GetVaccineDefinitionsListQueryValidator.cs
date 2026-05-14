using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.VaccineDefinitions.Queries.GetList;
using FluentValidation;

namespace Backend.Veteriner.Application.VaccineDefinitions.Queries.GetList.Validators;

public sealed class GetVaccineDefinitionsListQueryValidator : AbstractValidator<GetVaccineDefinitionsListQuery>
{
    public GetVaccineDefinitionsListQueryValidator()
    {
        RuleFor(x => x.PageRequest).NotNull();
        RuleFor(x => x.PageRequest.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageRequest.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.PageRequest.Search)
            .MaximumLength(ListQueryTextSearch.MaxTermLength)
            .When(x => x.PageRequest.Search != null);
    }
}
