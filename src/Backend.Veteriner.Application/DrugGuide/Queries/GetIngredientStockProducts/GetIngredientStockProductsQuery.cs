using Backend.Veteriner.Application.DrugGuide.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.DrugGuide.Queries.GetIngredientStockProducts;

/// <summary>
/// Etken maddeyi içeren ruhsatlı ürünlere bağlı klinik ürün kartları ve aktif klinikteki stok miktarı (REF-001 Dilim 2).
/// Stok satırı olmayan ürün 0 ile döner.
/// </summary>
public sealed record GetIngredientStockProductsQuery(Guid ActiveIngredientId, Guid? ClinicId = null)
    : IRequest<Result<IReadOnlyList<IngredientStockProductDto>>>;
