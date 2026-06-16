namespace Backend.Veteriner.Infrastructure.Projections.Appointments;

public sealed record AppointmentProjectionRebuildResult(
    bool Success,
    int CommandAppointmentCount,
    int QueryAppointmentCount,
    int PetActivityCount,
    int ClientActivityCount,
    int DailyStatsCount,
    int PendingAppointmentOutboxCount,
    int DeadLetterAppointmentOutboxCount,
    TimeSpan Duration);
