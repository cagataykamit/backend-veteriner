using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.StockMovements.Contracts.Dtos;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.StockMovements.Commands.Create;

/// <summary>
/// Parti alanları opsiyoneldir: <see cref="Batch"/> yalnız <c>In</c>/<c>Initial</c> girişte yeni parti açar;
/// <see cref="BatchId"/> yalnız <c>Out</c>/<c>Adjustment</c> için düşülecek/sayılacak partidir (yoksa FEFO).
/// <see cref="NotExpiredOn"/> yalnız uygulama kaydı tarafından kod içinden verilir (süresi geçmiş partiyi engeller); gövdeden bağlanmaz.
/// </summary>
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
    string? Notes,
    Guid? RequestId = null,
    StockBatchDetails? Batch = null,
    Guid? BatchId = null,
    [property: JsonIgnore] DateOnly? NotExpiredOn = null)
    : IRequest<Result<StockMovementCreateResult>>, ITransactionalRollbackOnFailureRequest;
