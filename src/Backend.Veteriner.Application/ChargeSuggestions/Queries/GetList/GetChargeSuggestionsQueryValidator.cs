using FluentValidation;

namespace Backend.Veteriner.Application.ChargeSuggestions.Queries.GetList;

public sealed class GetChargeSuggestionsQueryValidator : AbstractValidator<GetChargeSuggestionsQuery>
{
    public GetChargeSuggestionsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}
