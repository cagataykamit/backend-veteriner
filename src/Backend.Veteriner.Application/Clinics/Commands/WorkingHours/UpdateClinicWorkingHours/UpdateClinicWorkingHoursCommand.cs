using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Clinics.Commands.WorkingHours.UpdateClinicWorkingHours;

/// <summary>
/// Mevcut hafta silinip yeni satırlar yazılır; tek veritabanı işlemi. Geçersiz bir gün (iş kuralı hatası)
/// akışı yarıda keserse silme de geri alınır, klinik eski çalışma saatleriyle kalır.
/// </summary>
public sealed record UpdateClinicWorkingHoursCommand(Guid ClinicId, IReadOnlyList<ClinicWorkingHourDto> Items)
    : IRequest<Result<IReadOnlyList<ClinicWorkingHourDto>>>, ITransactionalRollbackOnFailureRequest;
