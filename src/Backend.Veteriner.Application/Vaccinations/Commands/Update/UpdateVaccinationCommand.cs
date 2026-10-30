using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using Mediator;

namespace Backend.Veteriner.Application.Vaccinations.Commands.Update;

public sealed record UpdateVaccinationCommand(
    Guid Id,
    Guid ClinicId,
    Guid PetId,
    Guid? ExaminationId,
    Guid VaccineDefinitionId,
    VaccinationStatus Status,
    DateTime? AppliedAtUtc,
    DateTime? DueAtUtc,
    string? Notes,
    AdministrationRoute? Route = null,
    string? Site = null)
    : IRequest<Result>;
