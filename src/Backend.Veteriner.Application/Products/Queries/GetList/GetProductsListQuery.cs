using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Products.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Products.Queries.GetList;

/// <param name="Barcode">Tam eşleşme (TIER-006); <c>Search</c> ile birlikte verilirse ikisi de uygulanır.</param>
public sealed record GetProductsListQuery(
    PageRequest PageRequest,
    Guid? ProductCategoryId = null,
    bool? IsActive = null,
    string? Barcode = null)
    : IRequest<Result<PagedResult<ProductDto>>>;
