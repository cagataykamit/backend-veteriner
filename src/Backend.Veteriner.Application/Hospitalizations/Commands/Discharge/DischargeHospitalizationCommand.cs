using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Hospitalizations.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Hospitalizations.Commands.Discharge;

/// <summary>
/// <see cref="DischargeNote"/>: null = dokunma, boş = temizle, dolu = taburcu notunu yazar; giriş notuna (Notes) dokunulmaz.
/// <see cref="RowVersion"/> geçiş döneminde opsiyoneldir (verilirse denetlenir).
/// </summary>
public sealed record DischargeHospitalizationCommand(
    Guid Id,
    DateTime DischargedAtUtc,
    string? DischargeNote,
    string? RowVersion = null)
    : IRequest<Result<HospitalizationWriteResultDto>>, ITransactionalRequest, IAuditableRequest
{
    public string AuditAction => "Hospitalization.Discharge";
    public string? AuditTarget => $"HospitalizationId={Id}";
}
