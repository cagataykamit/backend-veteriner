using Backend.Veteriner.Application.StockMovements.Queries.GetByProductId;
using Backend.Veteriner.Domain.Products;
using FluentValidation;

namespace Backend.Veteriner.Application.StockMovements.Queries.GetByProductId.Validators;

public sealed class GetStockMovementsByProductIdQueryValidator : AbstractValidator<GetStockMovementsByProductIdQuery>
{
    public GetStockMovementsByProductIdQueryValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();

        RuleFor(x => x.PageRequest).NotNull();
        RuleFor(x => x.PageRequest.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageRequest.PageSize).InclusiveBetween(1, 200);

        RuleFor(x => x.ClinicId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("ClinicId geçersiz.");

        RuleFor(x => x.MovementType)
            .Must(mt => !mt.HasValue || Enum.IsDefined(typeof(StockMovementType), mt.Value))
            .WithMessage("MovementType geçersiz.");

        RuleFor(x => x)
            .Must(x => !x.DateFromUtc.HasValue || !x.DateToUtc.HasValue || x.DateFromUtc <= x.DateToUtc)
            .WithMessage("DateFromUtc, DateToUtc'den büyük olamaz.");
    }
}
