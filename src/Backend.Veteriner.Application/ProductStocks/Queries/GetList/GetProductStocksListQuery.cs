using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.ProductStocks.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.ProductStocks.Queries.GetList;

public sealed record GetProductStocksListQuery(
    PageRequest PageRequest,
    Guid? ClinicId = null,
    Guid? ProductCategoryId = null,
    Guid? ProductId = null,
    bool? IsBelowMinimum = null,
    bool? IsActiveProduct = null)
    : IRequest<Result<PagedResult<ProductStockDto>>>;
