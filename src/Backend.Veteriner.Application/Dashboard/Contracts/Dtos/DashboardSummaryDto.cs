namespace Backend.Veteriner.Application.Dashboard.Contracts.Dtos;

/// <summary>
/// Klinik paneli özeti. Gunluk kutular ve randevu listesi operasyon saat dilimi gun sinirina gore hesaplanir,
/// tarih alanlari ise istemci tarafinda donusturulmak uzere UTC olarak doner.
/// </summary>
public sealed record DashboardSummaryDto(
    int TodayAppointmentsCount,
    int UpcomingAppointmentsCount,
    int CompletedTodayCount,
    int CancelledTodayCount,
    int TotalClientsCount,
    int TotalPetsCount,
    IReadOnlyList<DashboardAppointmentItemDto> UpcomingAppointments,
    IReadOnlyList<DashboardRecentClientDto> RecentClients,
    IReadOnlyList<DashboardRecentPetDto> RecentPets);
