using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Examinations.Commands.Update;

public sealed record UpdateExaminationCommand(
    Guid Id,
    Guid? ClinicId,
    Guid? PetId,
    Guid? AppointmentId,
    DateTime ExaminedAtUtc,
    string VisitReason,
    string Findings,
    string? Assessment,
    string? Notes)
    : IRequest<Result>;

