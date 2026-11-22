using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.DrugGuide.Contracts.Dtos;
using Backend.Veteriner.Application.DrugGuide.Specs;
using Backend.Veteriner.Domain.DrugGuide;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.DrugGuide.Queries.SearchIngredients;

public sealed class SearchActiveIngredientsQueryHandler
    : IRequestHandler<SearchActiveIngredientsQuery, Result<IReadOnlyList<ActiveIngredientDto>>>
{
    private readonly IReadRepository<ActiveIngredient> _ingredients;
    private readonly IReadRepository<LicensedProductIngredient> _links;

    public SearchActiveIngredientsQueryHandler(IReadRepository<ActiveIngredient> ingredients, IReadRepository<LicensedProductIngredient> links)
    {
        _ingredients = ingredients;
        _links = links;
    }

    public async ValueTask<Result<IReadOnlyList<ActiveIngredientDto>>> Handle(SearchActiveIngredientsQuery request, CancellationToken ct)
    {
        var tokens = ListQueryTextSearch.BuildKeyTokenPatterns(request.Search, DrugGuideErrors.MaxSearchTokens);
        var rows = await _ingredients.ListAsync(new ActiveIngredientsSearchSpec(tokens, Math.Clamp(request.Limit, 1, 50)), ct);
        if (rows.Count == 0)
            return Result<IReadOnlyList<ActiveIngredientDto>>.Success([]);

        var links = await _links.ListAsync(new LicensedProductIngredientsByIngredientSpec(rows.Select(r => r.Id).ToList()), ct);
        var counts = links.GroupBy(l => l.ActiveIngredientId).ToDictionary(g => g.Key, g => g.Count());

        IReadOnlyList<ActiveIngredientDto> result = rows
            .Select(r => new ActiveIngredientDto(r.Id, r.Name, counts.GetValueOrDefault(r.Id)))
            .ToList();
        return Result<IReadOnlyList<ActiveIngredientDto>>.Success(result);
    }
}
