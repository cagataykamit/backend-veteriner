using Backend.Veteriner.Application.ProductStocks.Queries.GetByProductId;
using FluentValidation;

namespace Backend.Veteriner.Application.ProductStocks.Queries.GetByProductId.Validators;

public sealed class GetProductStocksByProductIdQueryValidator : AbstractValidator<GetProductStocksByProductIdQuery>
{
    public GetProductStocksByProductIdQueryValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
    }
}
