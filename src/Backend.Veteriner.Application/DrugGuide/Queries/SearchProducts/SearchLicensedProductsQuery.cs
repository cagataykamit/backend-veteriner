using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.DrugGuide.Contracts.Dtos;
using Backend.Veteriner.Application.DrugGuide.Specs;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.DrugGuide.Queries.SearchProducts;

/// <summary>Rehber araması: ürün adı, etken madde ya da ruhsat sahibine göre; isteğe bağlı tek etken maddeyle daraltılır.</summary>
public sealed record SearchLicensedProductsQuery(
    PageRequest PageRequest,
    LicensedProductSearchField Field = LicensedProductSearchField.All,
    Guid? ActiveIngredientId = null)
    : IRequest<Result<PagedResult<LicensedProductDto>>>;
