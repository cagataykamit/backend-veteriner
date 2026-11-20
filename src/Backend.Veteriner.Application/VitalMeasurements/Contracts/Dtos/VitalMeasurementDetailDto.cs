namespace Backend.Veteriner.Application.VitalMeasurements.Contracts.Dtos;

public sealed record VitalMeasurementDetailDto(
    Guid Id,
    Guid TenantId,
    Guid ClinicId,
    Guid PetId,
    string PetName,
    Guid? HospitalizationId,
    Guid? ExaminationId,
    DateTime MeasuredAtUtc,
    decimal? TemperatureC,
    int? HeartRateBpm,
    int? RespiratoryRatePerMin,
    decimal? WeightKg,
    string? Note,
    Guid CreatedByUserId,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    string RowVersion,
    DateTime? CancelledAtUtc,
    string? CancelReason,
    bool AddedAfterFinalization,
    string? LateEntryReason);
