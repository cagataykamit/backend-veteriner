using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.ProductStocks.Queries.GetList;
using FluentValidation;

namespace Backend.Veteriner.Application.ProductStocks.Queries.GetList.Validators;

public sealed class GetProductStocksListQueryValidator : AbstractValidator<GetProductStocksListQuery>
{
    public GetProductStocksListQueryValidator()
    {
        RuleFor(x => x.PageRequest).NotNull();
        RuleFor(x => x.PageRequest.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageRequest.PageSize).InclusiveBetween(1, 200);

        RuleFor(x => x.PageRequest.Search)
            .MaximumLength(ListQueryTextSearch.MaxTermLength)
            .When(x => x.PageRequest.Search != null);

        RuleFor(x => x.ClinicId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("ClinicId geçersiz.");

        RuleFor(x => x.ProductCategoryId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("ProductCategoryId geçersiz.");

        RuleFor(x => x.ProductId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("ProductId geçersiz.");
    }
}
