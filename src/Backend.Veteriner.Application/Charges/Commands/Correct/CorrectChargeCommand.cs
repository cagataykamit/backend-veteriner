using Backend.Veteriner.Application.Charges.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Charges.Commands.Correct;

/// <summary>
/// PUT /api/v1/charges/{id}. Açıklama, miktar ve birim fiyat düzeltilir; tutar domain'de yeniden hesaplanır.
/// <c>Charges.Correct</c> yetkisi ve <see cref="Reason"/> (5–500 karakter) gerekir; yeni tutar tahsis edilmiş tutarın altına inemez.
/// <see cref="Tax"/> null ise satır türü ve KDV oranına dokunulmaz (ADR-036).
/// </summary>
public sealed record CorrectChargeCommand(
    Guid Id,
    string? Description,
    decimal Quantity,
    decimal UnitPrice,
    string? Reason,
    ChargeTaxDto? Tax = null)
    : IRequest<Result<ChargeDto>>, IAuditableRequest
{
    public string AuditAction => "Charge.Correct";
    public string? AuditTarget => $"ChargeId={Id}";
}
