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
    IReadOnlyList<DashboardRecentPetDto> RecentPets,
    // Faz 6B: son 7 takvim gününün randevu sayıları (sparkline için). İstanbul yerel günü bazında,
    // bugün dahil, oldest→newest sıralı, tam 7 eleman — boş günler 0 ile doldurulur.
    // Semantik §27.11: tüm statüler (Scheduled+Completed+Cancelled) dahil; TodayAppointmentsCount yalnız
    // Scheduled statüsü saydığı için bu iki alanın semantik tanımı farklıdır, çakışma değildir.
    IReadOnlyList<DashboardDailyCountDto> Last7DaysAppointments);
