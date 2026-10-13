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

        RuleFor(x => x.MovementType)
            .NotEqual(StockMovementType.Reversal)
            .WithMessage("Reversal tipi bu uçla oluşturulamaz; geri alma ucunu kullanın.");

        RuleFor(x => x.ReferenceId)
            .NotEmpty()
            .When(x => !string.IsNullOrWhiteSpace(x.ReferenceType))
            .WithMessage("ReferenceType verilirse ReferenceId de verilmelidir.");

        RuleFor(x => x.ReferenceType)
            .NotEmpty()
            .When(x => x.ReferenceId.HasValue && x.ReferenceId.Value != Guid.Empty)
            .WithMessage("ReferenceId verilirse ReferenceType de verilmelidir.");

        RuleFor(x => x.Quantity)
            .GreaterThan(0)
            .When(x => x.MovementType != StockMovementType.Adjustment)
            .WithMessage("Quantity sıfırdan büyük olmalıdır.");

        RuleFor(x => x.Quantity)
            .GreaterThanOrEqualTo(0)
            .When(x => x.MovementType == StockMovementType.Adjustment)
            .WithMessage("Adjustment için Quantity (hedef stok) negatif olamaz.");

        RuleFor(x => x.Quantity)
            .LessThanOrEqualTo(StockMovement.MaxQuantity)
            .WithMessage($"Quantity en fazla {StockMovement.MaxQuantity:0} olabilir.");

        RuleFor(x => x.UnitCost)
            .Must(uc => !uc.HasValue || uc.Value >= 0)
            .WithMessage("UnitCost negatif olamaz.");

        RuleFor(x => x.Batch)
            .Null()
            .When(x => x.MovementType is StockMovementType.Out or StockMovementType.Adjustment)
            .WithMessage("Parti bilgisi yalnız stok girişinde (In/Initial) verilir; çıkış ve sayımda batchId kullanın.");

        RuleFor(x => x.BatchId)
            .Null()
            .When(x => x.MovementType is StockMovementType.In or StockMovementType.Initial)
            .WithMessage("Giriş her zaman yeni parti açar; batchId yalnız Out/Adjustment için verilir.");

        RuleFor(x => x.BatchId)
            .NotEqual(Guid.Empty)
            .When(x => x.BatchId.HasValue)
            .WithMessage("BatchId geçersiz.");

        When(x => x.Batch is not null, () =>
        {
            RuleFor(x => x.Batch!.LotNumber).MaximumLength(StockBatch.MaxLotNumberLength);
            RuleFor(x => x.Batch!.SupplierName).MaximumLength(StockBatch.MaxSupplierNameLength);
            RuleFor(x => x.Batch!.InvoiceNumber).MaximumLength(StockBatch.MaxInvoiceNumberLength);
            RuleFor(x => x.Batch!.TransportMethod).MaximumLength(StockBatch.MaxTransportLength);
            RuleFor(x => x.Batch!.ColdChainMonitor).MaximumLength(StockBatch.MaxTransportLength);
        });

        RuleFor(x => x.Reason).MaximumLength(ReasonMax).When(x => x.Reason != null);
        RuleFor(x => x.ReferenceType).MaximumLength(ReferenceTypeMax).When(x => x.ReferenceType != null);
        RuleFor(x => x.Notes).MaximumLength(NotesMax).When(x => x.Notes != null);
    }
}
