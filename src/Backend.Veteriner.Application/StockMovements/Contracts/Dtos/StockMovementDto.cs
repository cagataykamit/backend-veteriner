using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.StockMovements.Contracts.Dtos;

/// <summary>Stok hareketi okuma modeli.</summary>
public sealed record StockMovementDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string? ProductSku,
    Guid? ProductCategoryId,
    string? ProductCategoryName,
    Guid ClinicId,
    string ClinicName,
    StockMovementType MovementType,
    decimal Quantity,
    decimal? UnitCost,
    string? Reason,
    string? ReferenceType,
    Guid? ReferenceId,
    DateTime OccurredAtUtc,
    Guid? CreatedByUserId,
    string? Notes,
    DateTime CreatedAtUtc);
