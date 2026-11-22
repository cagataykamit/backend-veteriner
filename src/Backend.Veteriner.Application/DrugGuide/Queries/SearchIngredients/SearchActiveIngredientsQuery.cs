using Backend.Veteriner.Application.DrugGuide.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.DrugGuide.Queries.SearchIngredients;

/// <summary>Etken madde araması (ada göre, en çok <see cref="Limit"/>); her satırda o maddeyi içeren ürün sayısı.</summary>
public sealed record SearchActiveIngredientsQuery(string? Search, int Limit = 20) : IRequest<Result<IReadOnlyList<ActiveIngredientDto>>>;
