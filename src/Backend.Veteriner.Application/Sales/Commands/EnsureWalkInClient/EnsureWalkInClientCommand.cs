using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Sales.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Sales.Commands.EnsureWalkInClient;

/// <summary>
/// POST /api/v1/sales/walk-in-client. Kiracının peşin müşterisini döndürür, yoksa oluşturur (idempotent).
/// Satış komutu müşteri verilmediğinde aynı komutu kullanır; kural tek yerde yaşar.
/// </summary>
public sealed record EnsureWalkInClientCommand
    : IRequest<Result<WalkInClientDto>>, IAuditableRequest
{
    public string AuditAction => "Sale.EnsureWalkInClient";
    public string? AuditTarget => null;
}
