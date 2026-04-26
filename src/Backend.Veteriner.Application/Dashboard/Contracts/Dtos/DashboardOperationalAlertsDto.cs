namespace Backend.Veteriner.Application.Dashboard.Contracts.Dtos;

public sealed record DashboardOperationalAlertsDto(
    int OverdueScheduledAppointmentsCount,
    int UpcomingAppointmentsNext24HoursCount,
    int TodayCancelledAppointmentsCount,
    int OverdueVaccinationsCount,
    int UpcomingVaccinationsNext7DaysCount);
