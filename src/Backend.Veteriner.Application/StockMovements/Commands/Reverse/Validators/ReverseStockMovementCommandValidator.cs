using FluentValidation;

namespace Backend.Veteriner.Application.StockMovements.Commands.Reverse.Validators;

public sealed class ReverseStockMovementCommandValidator : AbstractValidator<ReverseStockMovementCommand>
{
    private const int ReasonMax = 500;
    private const int NotesMax = 4000;

    public ReverseStockMovementCommandValidator()
    {
        RuleFor(x => x.MovementId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(ReasonMax);
        RuleFor(x => x.Notes).MaximumLength(NotesMax).When(x => x.Notes != null);
    }
}
