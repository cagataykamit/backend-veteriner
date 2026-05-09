using FluentValidation;

namespace Backend.Veteriner.Application.Products.Commands.Deactivate.Validators;

public sealed class DeactivateProductCommandValidator : AbstractValidator<DeactivateProductCommand>
{
    public DeactivateProductCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
