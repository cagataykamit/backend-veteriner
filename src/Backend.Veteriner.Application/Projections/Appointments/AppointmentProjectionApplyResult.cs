namespace Backend.Veteriner.Application.Projections.Appointments;

public sealed class AppointmentProjectionApplyResult
{
    private AppointmentProjectionApplyResult(bool isDuplicate, double lagMs)
    {
        IsDuplicate = isDuplicate;
        LagMs = lagMs;
    }

    public bool IsDuplicate { get; }

    public double LagMs { get; }

    public static AppointmentProjectionApplyResult DuplicateSkipped() => new(isDuplicate: true, lagMs: 0);

    public static AppointmentProjectionApplyResult Applied(double lagMs) => new(isDuplicate: false, lagMs: lagMs);
}
