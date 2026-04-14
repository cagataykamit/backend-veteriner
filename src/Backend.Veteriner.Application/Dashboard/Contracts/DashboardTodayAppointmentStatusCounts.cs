namespace Backend.Veteriner.Application.Dashboard.Contracts;

/// <summary>
/// Aynı UTC gün penceresindeki randevu durum sayıları (dashboard özet tek sorgu optimizasyonu).
/// </summary>
public readonly record struct DashboardTodayAppointmentStatusCounts(
    int Scheduled,
    int Completed,
    int Cancelled);
