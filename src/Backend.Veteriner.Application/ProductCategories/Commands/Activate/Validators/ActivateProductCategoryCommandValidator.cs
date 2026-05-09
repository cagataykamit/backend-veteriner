using FluentValidation;

namespace Backend.Veteriner.Application.ProductCategories.Commands.Activate.Validators;

public sealed class ActivateProductCategoryCommandValidator : AbstractValidator<ActivateProductCategoryCommand>
{
    public ActivateProductCategoryCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
