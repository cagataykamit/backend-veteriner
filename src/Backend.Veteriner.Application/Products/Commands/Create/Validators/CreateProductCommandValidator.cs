using FluentValidation;

namespace Backend.Veteriner.Application.Products.Commands.Create.Validators;

public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Unit).NotEmpty().MaximumLength(50);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Currency).NotEmpty().MaximumLength(3);
        RuleFor(x => x.Sku).MaximumLength(100);
        RuleFor(x => x.Barcode).MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.Kind).IsInEnum().When(x => x.Kind.HasValue);
        RuleFor(x => x.ControlledClass).IsInEnum().When(x => x.ControlledClass.HasValue);
        RuleFor(x => x.PharmacologicalForm).MaximumLength(100);
        RuleFor(x => x.PresentationForm).MaximumLength(100);
        RuleFor(x => x.Packaging).MaximumLength(100);
    }
}
