using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using Mediator;

namespace Backend.Veteriner.Application.Vaccinations.Commands.Update;

/// <summary>
/// PUT /api/v1/vaccinations/{id}. <see cref="RowVersion"/> zorunludur (ADR-043 K5); durum geçişi yalnız Planlı → Uygulandı,
/// iptal ayrı eylemdir (<c>POST /vaccinations/{id}/cancel</c>).
/// </summary>
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
    string? Site = null,
    string? RowVersion = null)
    : IRequest<Result>;
