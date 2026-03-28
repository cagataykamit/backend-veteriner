using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using MediatR;

namespace Backend.Veteriner.Application.Vaccinations.Commands.Update;

public sealed record UpdateVaccinationCommand(
    Guid Id,
    Guid ClinicId,
    Guid PetId,
    Guid? ExaminationId,
    string VaccineName,
    VaccinationStatus Status,
    DateTime? AppliedAtUtc,
    DateTime? DueAtUtc,
    string? Notes)
    : IRequest<Result>;
