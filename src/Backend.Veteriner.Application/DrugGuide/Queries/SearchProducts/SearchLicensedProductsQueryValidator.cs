using Backend.Veteriner.Application.Common;
using FluentValidation;

namespace Backend.Veteriner.Application.DrugGuide.Queries.SearchProducts;

public sealed class SearchLicensedProductsQueryValidator : AbstractValidator<SearchLicensedProductsQuery>
{
    public SearchLicensedProductsQueryValidator()
    {
        RuleFor(x => x.PageRequest).NotNull();
        RuleFor(x => x.PageRequest.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageRequest.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.PageRequest.Search)
            .MaximumLength(ListQueryTextSearch.MaxTermLength)
            .When(x => x.PageRequest.Search != null);
        RuleFor(x => x.Field).IsInEnum();
        RuleFor(x => x.ActiveIngredientId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Etken madde geçersiz.");
    }
}
