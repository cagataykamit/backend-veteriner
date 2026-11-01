using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Visits.Commands.Create;

/// <summary>
/// Geliş kaydı. Randevulu: <see cref="AppointmentId"/> dolu, klinik/hayvan randevudan türetilir.
/// Randevusuz: <see cref="ClinicId"/> (veya klinik bağlamı) ve <see cref="PetId"/> zorunlu; randevu oluşturulmaz.
/// Hazırlık (CHECKIN-001/002/003, isteğe bağlı): <see cref="ArrivalReason"/> ve <see cref="PreparedExaminationTemplateId"/>; tekrar
/// istekte mevcut geliş döner ve hazırlık değişmez (değişiklik <c>PUT /visits/{id}/preparation</c> ile).
/// </summary>
public sealed record CreateVisitCommand(
    Guid? ClinicId,
    Guid? PetId,
    Guid? AppointmentId,
    Guid? ResponsibleVeterinarianUserId,
    bool IsUrgent = false,
    string? ArrivalReason = null,
    Guid? PreparedExaminationTemplateId = null)
    : IRequest<Result<VisitCreateResultDto>>;
