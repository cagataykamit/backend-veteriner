using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.StockMovements.Contracts.Dtos;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.StockMovements.Commands.Create;

public sealed record CreateStockMovementCommand(
    Guid ClinicId,
    Guid ProductId,
    StockMovementType MovementType,
    decimal Quantity,
    decimal? UnitCost,
    string? Reason,
    string? ReferenceType,
    Guid? ReferenceId,
    DateTime? OccurredAtUtc,
    string? Notes)
    : IRequest<Result<StockMovementDto>>, ITransactionalRequest;
