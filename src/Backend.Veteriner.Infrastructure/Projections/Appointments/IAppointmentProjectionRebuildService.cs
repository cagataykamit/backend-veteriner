namespace Backend.Veteriner.Infrastructure.Projections.Appointments;

public interface IAppointmentProjectionRebuildService
{
    Task<AppointmentProjectionRebuildResult> RebuildAsync(
        int batchSize,
        CancellationToken cancellationToken = default);
}
