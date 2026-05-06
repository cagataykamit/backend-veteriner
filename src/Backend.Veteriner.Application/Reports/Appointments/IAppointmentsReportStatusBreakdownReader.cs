using Backend.Veteriner.Application.Reports.Appointments.Contracts.Dtos;

namespace Backend.Veteriner.Application.Reports.Appointments;

/// <summary>
/// Randevu raporu için tarih/klinik/pet/arama filtreleriyle (durum filtresi olmadan) status bazında sayım.
/// </summary>
public interface IAppointmentsReportStatusBreakdownReader
{
    Task<IReadOnlyList<AppointmentStatusCountRow>> GetAsync(
        Guid tenantId,
        Guid? clinicId,
        Guid? petId,
        IReadOnlyList<Guid>? restrictedPetIdsForClient,
        DateTime fromUtc,
        DateTime toUtc,
        string? searchContainsLikePattern,
        Guid[] searchPetIds,
        CancellationToken ct = default);
}
