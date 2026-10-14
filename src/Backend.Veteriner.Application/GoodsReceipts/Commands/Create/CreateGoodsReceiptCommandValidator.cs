using FluentValidation;

namespace Backend.Veteriner.Application.GoodsReceipts.Commands.Create;

/// <summary>Yapısal kurallar; satır ve parti kuralları mevcut stok girişi komutunda tek yerde yaşar.</summary>
public sealed class CreateGoodsReceiptCommandValidator : AbstractValidator<CreateGoodsReceiptCommand>
{
    public CreateGoodsReceiptCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.RequestId).NotEmpty().WithErrorCode("GoodsReceipts.RequestIdRequired");
        RuleFor(x => x.Lines).NotEmpty();
        RuleFor(x => x.Lines!)
            .Must(lines => lines.Count <= CreateGoodsReceiptCommand.MaxLines)
            .When(x => x.Lines is not null)
            .WithMessage($"En fazla {CreateGoodsReceiptCommand.MaxLines} satır girilebilir.");
    }
}
