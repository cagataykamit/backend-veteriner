namespace Backend.Veteriner.Application.VitalMeasurements.Contracts.Dtos;

public sealed record VitalMeasurementListItemDto(
    Guid Id,
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
    bool IsCancelled,
    bool AddedAfterFinalization);
