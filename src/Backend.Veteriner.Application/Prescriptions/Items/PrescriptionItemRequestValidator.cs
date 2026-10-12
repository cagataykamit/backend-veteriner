using Backend.Veteriner.Application.Prescriptions.Contracts.Dtos;
using Backend.Veteriner.Domain.Prescriptions;
using FluentValidation;

namespace Backend.Veteriner.Application.Prescriptions.Items;

public sealed class PrescriptionItemRequestValidator : AbstractValidator<PrescriptionItemRequest>
{
    public PrescriptionItemRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required when ProductId is not set.")
            .When(x => !x.ProductId.HasValue);
        RuleFor(x => x.Name).MaximumLength(PrescriptionItem.NameMaxLength);
        RuleFor(x => x.ProductId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("ProductId is invalid.");
        RuleFor(x => x.Instructions).MaximumLength(PrescriptionItem.InstructionsMaxLength);
        RuleFor(x => x.Quantity)
            .GreaterThan(0)
            .LessThanOrEqualTo(PrescriptionItem.QuantityMax)
            .When(x => x.Quantity.HasValue);
    }
}
