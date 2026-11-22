using Backend.Veteriner.Application.Common;
using FluentValidation;

namespace Backend.Veteriner.Application.DrugGuide.Queries.SearchIngredients;

public sealed class SearchActiveIngredientsQueryValidator : AbstractValidator<SearchActiveIngredientsQuery>
{
    public SearchActiveIngredientsQueryValidator()
    {
        RuleFor(x => x.Search)
            .MaximumLength(ListQueryTextSearch.MaxTermLength)
            .When(x => x.Search != null);
        RuleFor(x => x.Limit).InclusiveBetween(1, 50);
    }
}
