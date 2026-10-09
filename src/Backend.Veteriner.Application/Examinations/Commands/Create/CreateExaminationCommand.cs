using Backend.Veteriner.Application.Examinations.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Examinations.Commands.Create;

public sealed record CreateExaminationCommand(
    Guid? ClinicId,
    Guid? PetId,
    Guid? AppointmentId,
    DateTime ExaminedAtUtc,
    string VisitReason,
    string Findings,
    string? Assessment,
    string? Notes,
    string? Anamnesis = null,
    string? Plan = null,
    decimal? WeightKg = null,
    decimal? TemperatureC = null,
    int? HeartRateBpm = null,
    int? RespiratoryRatePerMin = null,
    DateTime? VitalsMeasuredAtUtc = null)
    : IRequest<Result<ExaminationWriteResultDto>>;
