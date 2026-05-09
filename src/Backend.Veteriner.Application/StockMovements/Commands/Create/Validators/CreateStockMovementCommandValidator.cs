using Backend.Veteriner.Application.StockMovements.Commands.Create;
using Backend.Veteriner.Domain.Products;
using FluentValidation;

namespace Backend.Veteriner.Application.StockMovements.Commands.Create.Validators;

public sealed class CreateStockMovementCommandValidator : AbstractValidator<CreateStockMovementCommand>
{
    private const int ReasonMax = 500;
    private const int ReferenceTypeMax = 100;
    private const int NotesMax = 4000;

    public CreateStockMovementCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();

        RuleFor(x => x.MovementType)
            .Must(mt => Enum.IsDefined(typeof(StockMovementType), mt))
            .WithMessage("MovementType geçersiz.");

        RuleFor(x => x.Quantity)
            .GreaterThan(0)
            .When(x => x.MovementType != StockMovementType.Adjustment)
            .WithMessage("Quantity sıfırdan büyük olmalıdır.");

        RuleFor(x => x.Quantity)
            .GreaterThanOrEqualTo(0)
            .When(x => x.MovementType == StockMovementType.Adjustment)
            .WithMessage("Adjustment için Quantity (hedef stok) negatif olamaz.");

        RuleFor(x => x.UnitCost)
            .Must(uc => !uc.HasValue || uc.Value >= 0)
            .WithMessage("UnitCost negatif olamaz.");

        RuleFor(x => x.Reason).MaximumLength(ReasonMax).When(x => x.Reason != null);
        RuleFor(x => x.ReferenceType).MaximumLength(ReferenceTypeMax).When(x => x.ReferenceType != null);
        RuleFor(x => x.Notes).MaximumLength(NotesMax).When(x => x.Notes != null);
    }
}
