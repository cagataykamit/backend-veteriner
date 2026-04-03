using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Hospitalizations.Commands.Update;

public sealed record UpdateHospitalizationCommand(
    Guid Id,
    Guid ClinicId,
    Guid PetId,
    Guid? ExaminationId,
    DateTime AdmittedAtUtc,
    DateTime? PlannedDischargeAtUtc,
    string Reason,
    string? Notes)
    : IRequest<Result>;
