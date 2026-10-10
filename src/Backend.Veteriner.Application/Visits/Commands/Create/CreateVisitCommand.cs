using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Visits.Commands.Create;

/// <summary>
/// Geliş kaydı. Randevulu: <see cref="AppointmentId"/> dolu, klinik/hayvan randevudan türetilir.
/// Randevusuz: <see cref="ClinicId"/> (veya klinik bağlamı) ve <see cref="PetId"/> zorunlu; randevu oluşturulmaz.
/// </summary>
public sealed record CreateVisitCommand(
    Guid? ClinicId,
    Guid? PetId,
    Guid? AppointmentId,
    Guid? ResponsibleVeterinarianUserId,
    bool IsUrgent = false)
    : IRequest<Result<VisitCreateResultDto>>;
