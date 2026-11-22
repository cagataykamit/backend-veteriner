using FluentValidation;

namespace Backend.Veteriner.Application.DrugGuide.Queries.GetIngredientStockProducts;

public sealed class GetIngredientStockProductsQueryValidator : AbstractValidator<GetIngredientStockProductsQuery>
{
    public GetIngredientStockProductsQueryValidator()
    {
        RuleFor(x => x.ActiveIngredientId).NotEmpty();
        RuleFor(x => x.ClinicId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Klinik filtresi geçersiz.");
    }
}
