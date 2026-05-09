using FluentValidation;

namespace Backend.Veteriner.Application.Products.Commands.Activate.Validators;

public sealed class ActivateProductCommandValidator : AbstractValidator<ActivateProductCommand>
{
    public ActivateProductCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
