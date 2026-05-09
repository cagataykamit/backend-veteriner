using FluentValidation;

namespace Backend.Veteriner.Application.ProductCategories.Queries.GetById.Validators;

public sealed class GetProductCategoryByIdQueryValidator : AbstractValidator<GetProductCategoryByIdQuery>
{
    public GetProductCategoryByIdQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
