using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.VitalMeasurements.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.VitalMeasurements.Commands.Update;

/// <summary>
/// PUT /api/v1/vital-measurements/{id}. Zaman, değerler ve not tam değiştirilir; hasta, klinik ve bağ değişmez.
/// <see cref="RowVersion"/> zorunludur (ADR-043 K5).
/// </summary>
public sealed record UpdateVitalMeasurementCommand(
    Guid Id,
    DateTime MeasuredAtUtc,
    decimal? TemperatureC,
    int? HeartRateBpm,
    int? RespiratoryRatePerMin,
    decimal? WeightKg,
    string? Note,
    string? RowVersion)
    : IRequest<Result<VitalMeasurementWriteResultDto>>, ITransactionalRequest, IAuditableRequest
{
    public string AuditAction => "VitalMeasurement.Update";
    public string? AuditTarget => $"VitalMeasurementId={Id}";
}
