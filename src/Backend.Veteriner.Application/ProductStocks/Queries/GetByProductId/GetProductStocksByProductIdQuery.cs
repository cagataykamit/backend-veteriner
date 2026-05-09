using Backend.Veteriner.Application.ProductStocks.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.ProductStocks.Queries.GetByProductId;

public sealed record GetProductStocksByProductIdQuery(Guid ProductId)
    : IRequest<Result<IReadOnlyList<ProductStockDto>>>;
