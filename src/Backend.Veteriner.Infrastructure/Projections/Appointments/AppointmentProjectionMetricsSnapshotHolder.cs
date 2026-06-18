namespace Backend.Veteriner.Infrastructure.Projections.Appointments;

public sealed class AppointmentProjectionMetricsSnapshotHolder
{
    private AppointmentProjectionMetricsSnapshot _current = AppointmentProjectionMetricsSnapshot.Empty;

    public AppointmentProjectionMetricsSnapshot Current
        => Volatile.Read(ref _current);

    public void Update(AppointmentProjectionMetricsSnapshot snapshot)
        => Volatile.Write(ref _current, snapshot);
}
