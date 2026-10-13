using Backend.Veteriner.Application.StockBatches.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.StockBatches.Queries.GetById;

public sealed record GetStockBatchByIdQuery(Guid Id) : IRequest<Result<StockBatchDto>>;
