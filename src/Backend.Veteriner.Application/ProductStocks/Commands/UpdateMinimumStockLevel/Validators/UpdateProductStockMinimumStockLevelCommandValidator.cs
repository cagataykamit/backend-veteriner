using FluentValidation;

namespace Backend.Veteriner.Application.ProductStocks.Commands.UpdateMinimumStockLevel.Validators;

public sealed class UpdateProductStockMinimumStockLevelCommandValidator
    : AbstractValidator<UpdateProductStockMinimumStockLevelCommand>
{
    public UpdateProductStockMinimumStockLevelCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.MinimumStockLevel).GreaterThanOrEqualTo(0);
    }
}
