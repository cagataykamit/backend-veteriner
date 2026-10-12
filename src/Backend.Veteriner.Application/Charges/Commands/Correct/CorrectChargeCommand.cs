using Backend.Veteriner.Application.Charges.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Charges.Commands.Correct;

/// <summary>
/// PUT /api/v1/charges/{id}. Açıklama, miktar ve birim fiyat düzeltilir; tutar domain'de yeniden hesaplanır.
/// <c>Charges.Correct</c> yetkisi ve <see cref="Reason"/> (5–500 karakter) gerekir; yeni tutar tahsis edilmiş tutarın altına inemez.
/// </summary>
public sealed record CorrectChargeCommand(
    Guid Id,
    string? Description,
    decimal Quantity,
    decimal UnitPrice,
    string? Reason)
    : IRequest<Result<ChargeDto>>, IAuditableRequest
{
    public string AuditAction => "Charge.Correct";
    public string? AuditTarget => $"ChargeId={Id}";
}
