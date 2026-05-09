using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.StockMovements.Queries.GetList;
using Backend.Veteriner.Domain.Products;
using FluentValidation;

namespace Backend.Veteriner.Application.StockMovements.Queries.GetList.Validators;

public sealed class GetStockMovementsListQueryValidator : AbstractValidator<GetStockMovementsListQuery>
{
    public GetStockMovementsListQueryValidator()
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

        RuleFor(x => x.ProductId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("ProductId geçersiz.");

        RuleFor(x => x.ProductCategoryId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("ProductCategoryId geçersiz.");

        RuleFor(x => x.MovementType)
            .Must(mt => !mt.HasValue || Enum.IsDefined(typeof(StockMovementType), mt.Value))
            .WithMessage("MovementType geçersiz.");

        RuleFor(x => x)
            .Must(x => !x.DateFromUtc.HasValue || !x.DateToUtc.HasValue || x.DateFromUtc <= x.DateToUtc)
            .WithMessage("DateFromUtc, DateToUtc'den büyük olamaz.");
    }
}
