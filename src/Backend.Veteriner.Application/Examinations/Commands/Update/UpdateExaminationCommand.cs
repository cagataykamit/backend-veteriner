using Backend.Veteriner.Application.Examinations.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Examinations.Commands.Update;

/// <summary>
/// <see cref="RowVersion"/>: istemcinin formu açarken GET ile aldığı sürüm (Base64); zorunlu.
/// <see cref="AppointmentId"/> null ise mevcut randevu bağlantısı korunur.
/// </summary>
public sealed record UpdateExaminationCommand(
    Guid Id,
    Guid? ClinicId,
    Guid? PetId,
    Guid? AppointmentId,
    DateTime ExaminedAtUtc,
    string VisitReason,
    string Findings,
    string? Assessment,
    string? Notes,
    string? RowVersion = null,
    string? Anamnesis = null,
    string? Plan = null,
    decimal? WeightKg = null,
    decimal? TemperatureC = null,
    int? HeartRateBpm = null,
    int? RespiratoryRatePerMin = null,
    DateTime? VitalsMeasuredAtUtc = null)
    : IRequest<Result<ExaminationWriteResultDto>>;
