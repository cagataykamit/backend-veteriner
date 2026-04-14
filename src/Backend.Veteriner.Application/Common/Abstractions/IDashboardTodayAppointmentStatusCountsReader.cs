using Backend.Veteriner.Application.Dashboard.Contracts;

namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// Dashboard için bugün (operasyon günü) randevu durum sayılarını tek veritabanı round-trip ile okur.
/// </summary>
public interface IDashboardTodayAppointmentStatusCountsReader
{
    Task<DashboardTodayAppointmentStatusCounts> GetAsync(
        Guid tenantId,
        Guid? clinicId,
        DateTime dayStartUtc,
        DateTime dayEndUtc,
        CancellationToken ct = default);
}
