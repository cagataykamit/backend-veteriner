using Backend.Veteriner.Application.ProductStocks.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.ProductStocks.Commands.UpdateMinimumStockLevel;

public sealed record UpdateProductStockMinimumStockLevelCommand(Guid Id, decimal MinimumStockLevel)
    : IRequest<Result<ProductStockDto>>;
