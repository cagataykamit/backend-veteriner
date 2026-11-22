using FluentValidation;

namespace Backend.Veteriner.Application.DrugGuide.Queries.GetProductDrugInfo;

public sealed class GetProductDrugInfoQueryValidator : AbstractValidator<GetProductDrugInfoQuery>
{
    /// <summary>Reçete satır sınırıyla uyumlu üst sınır.</summary>
    public const int MaxProducts = 50;

    public GetProductDrugInfoQueryValidator()
    {
        RuleFor(x => x.ProductIds)
            .NotNull()
            .Must(ids => ids.Count is > 0 and <= MaxProducts)
            .WithMessage($"1–{MaxProducts} ürün verilmelidir.");
        RuleForEach(x => x.ProductIds).NotEmpty();
        RuleFor(x => x.PetId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Hasta geçersiz.");
    }
}
