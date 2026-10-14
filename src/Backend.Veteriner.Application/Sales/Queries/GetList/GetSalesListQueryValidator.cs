using FluentValidation;

namespace Backend.Veteriner.Application.Sales.Queries.GetList;

public sealed class GetSalesListQueryValidator : AbstractValidator<GetSalesListQuery>
{
    public GetSalesListQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
