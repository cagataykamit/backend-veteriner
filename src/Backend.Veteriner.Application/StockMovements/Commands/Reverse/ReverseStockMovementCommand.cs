using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.StockMovements.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.StockMovements.Commands.Reverse;

public sealed record ReverseStockMovementCommand(
    Guid MovementId,
    string Reason,
    string? Notes,
    Guid? RequestId)
    : IRequest<Result<StockMovementCreateResult>>, ITransactionalRequest;
