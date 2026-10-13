using FluentValidation;

namespace Backend.Veteriner.Application.StockBatches.Queries.GetList;

public sealed class GetStockBatchesListQueryValidator : AbstractValidator<GetStockBatchesListQuery>
{
    public GetStockBatchesListQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.LotNumber).MaximumLength(100);
    }
}
