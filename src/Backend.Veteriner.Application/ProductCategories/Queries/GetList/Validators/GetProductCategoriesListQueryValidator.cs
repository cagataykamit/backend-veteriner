using FluentValidation;

namespace Backend.Veteriner.Application.ProductCategories.Queries.GetList.Validators;

public sealed class GetProductCategoriesListQueryValidator : AbstractValidator<GetProductCategoriesListQuery>
{
    public GetProductCategoriesListQueryValidator()
    {
        RuleFor(x => x.PageRequest).NotNull();
        RuleFor(x => x.PageRequest.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageRequest.PageSize).InclusiveBetween(1, 200);
    }
}
