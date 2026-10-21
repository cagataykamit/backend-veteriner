using Backend.Veteriner.Application.Charges.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Charges.Commands.Create;

/// <summary>
/// POST /api/v1/charges. Zorunlu: <see cref="ClientId"/>, <see cref="Description"/>, <see cref="Quantity"/>, <see cref="UnitPrice"/>.
/// Opsiyonel: <see cref="ClinicId"/> (klinik bağlamı yoksa zorunlu), <see cref="PetId"/>, <see cref="VisitId"/>,
/// <see cref="ExaminationId"/>, <see cref="RequestId"/> (tekrar koruması), <see cref="Tax"/> (verilmezse hizmet + klinik
/// varsayılanı KDV, ADR-036). Tutar domain'de hesaplanır.
/// </summary>
public sealed record CreateChargeCommand(
    Guid? ClinicId,
    Guid ClientId,
    Guid? PetId,
    Guid? VisitId,
    Guid? ExaminationId,
    string? Description,
    decimal Quantity,
    decimal UnitPrice,
    Guid? RequestId,
    ChargeTaxDto? Tax = null)
    : IRequest<Result<ChargeCreateResultDto>>, IAuditableRequest
{
    public string AuditAction => "Charge.Create";
    public string? AuditTarget => $"ClientId={ClientId}";
}
