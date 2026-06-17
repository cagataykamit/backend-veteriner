using Backend.Veteriner.Application.Dashboard.Contracts;
using Backend.Veteriner.Application.Dashboard.Contracts.Dtos;
using Backend.Veteriner.Application.Dashboard.Specs;

namespace Backend.Veteriner.Application.Dashboard.ReadModels;

public sealed record DashboardAppointmentReadResult(
    DashboardTodayAppointmentStatusCounts TodayCounts,
    int UpcomingCount,
    IReadOnlyList<DashboardUpcomingAppointmentRow> UpcomingAppointments,
    IReadOnlyList<DashboardDailyCountDto> LastSevenDaysAppointments,
    int? ClinicScopedPetsTotal,
    int? ClinicScopedClientsTotal,
    IReadOnlyList<DashboardRecentPetRow> ClinicScopedRecentPets,
    IReadOnlyList<DashboardRecentClientRow> ClinicScopedRecentClients);
