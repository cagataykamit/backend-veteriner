namespace Backend.Veteriner.Application.Dashboard.ReadModels;

public interface IDashboardAppointmentReadModelReader
{
    Task<DashboardAppointmentReadResult> GetAsync(
        DashboardAppointmentReadRequest request,
        CancellationToken cancellationToken = default);
}
