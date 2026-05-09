using FluentValidation;

namespace Backend.Veteriner.Application.ProductCategories.Commands.Deactivate.Validators;

public sealed class DeactivateProductCategoryCommandValidator : AbstractValidator<DeactivateProductCategoryCommand>
{
    public DeactivateProductCategoryCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
