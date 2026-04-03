using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Hospitalizations.Commands.Create;

public sealed record CreateHospitalizationCommand(
    Guid ClinicId,
    Guid PetId,
    Guid? ExaminationId,
    DateTime AdmittedAtUtc,
    DateTime? PlannedDischargeAtUtc,
    string Reason,
    string? Notes)
    : IRequest<Result<Guid>>;
