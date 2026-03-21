namespace Backend.Veteriner.Application.Dashboard.Contracts.Dtos;

/// <summary>
/// Klinik paneli özeti. Tüm tarihler şimdilik <b>UTC</b> takvim günü / anına göredir; kiracı saat dilimi sonraya bırakıldı.
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
