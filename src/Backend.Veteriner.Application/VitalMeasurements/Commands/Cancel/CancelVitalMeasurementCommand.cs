using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.VitalMeasurements.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.VitalMeasurements.Commands.Cancel;

/// <summary>POST /api/v1/vital-measurements/{id}/cancel (ADR-043 K4). Gerekçe 5–500 karakter; <see cref="RowVersion"/> zorunludur.</summary>
public sealed record CancelVitalMeasurementCommand(Guid Id, string? Reason, string? RowVersion)
    : IRequest<Result<VitalMeasurementWriteResultDto>>, ITransactionalRequest, IAuditableRequest
{
    public string AuditAction => "VitalMeasurement.Cancel";
    public string? AuditTarget => $"VitalMeasurementId={Id}";
}
