using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.DrugGuide.Contracts.Dtos;
using Backend.Veteriner.Application.DrugGuide.Specs;
using Backend.Veteriner.Domain.DrugGuide;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.DrugGuide.Queries.GetSummary;

public sealed class GetDrugGuideSummaryQueryHandler : IRequestHandler<GetDrugGuideSummaryQuery, Result<DrugGuideSummaryDto>>
{
    private readonly IReadRepository<LicensedProduct> _products;
    private readonly IReadRepository<ActiveIngredient> _ingredients;

    public GetDrugGuideSummaryQueryHandler(IReadRepository<LicensedProduct> products, IReadRepository<ActiveIngredient> ingredients)
    {
        _products = products;
        _ingredients = ingredients;
    }

    public async ValueTask<Result<DrugGuideSummaryDto>> Handle(GetDrugGuideSummaryQuery request, CancellationToken ct)
    {
        var productCount = await _products.CountAsync(ct);
        var ingredientCount = await _ingredients.CountAsync(ct);
        DateTime? lastSynced = productCount == 0
            ? null
            : await _products.FirstOrDefaultAsync(new LicensedProductLatestSyncSpec(), ct);

        return Result<DrugGuideSummaryDto>.Success(new DrugGuideSummaryDto(productCount, ingredientCount, lastSynced));
    }
}
