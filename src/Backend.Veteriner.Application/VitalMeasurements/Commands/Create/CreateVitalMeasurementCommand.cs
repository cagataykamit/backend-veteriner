using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.VitalMeasurements.Commands.Create;

/// <summary>
/// POST /api/v1/vital-measurements (HOSP-004). Ölçüm yatışa ya da muayeneye bağlanabilir (ikisine birden değil).
/// <see cref="LateEntryReason"/> bağlı muayene kesinleşmişse zorunludur (5–500; ADR-043 AK-6).
/// </summary>
public sealed record CreateVitalMeasurementCommand(
    Guid ClinicId,
    Guid PetId,
    Guid? HospitalizationId,
    Guid? ExaminationId,
    DateTime MeasuredAtUtc,
    decimal? TemperatureC,
    int? HeartRateBpm,
    int? RespiratoryRatePerMin,
    decimal? WeightKg,
    string? Note,
    string? LateEntryReason = null)
    : IRequest<Result<Guid>>, ITransactionalRequest, IAuditableRequest
{
    public string AuditAction => "VitalMeasurement.Create";
    public string? AuditTarget => $"PetId={PetId}";
}
