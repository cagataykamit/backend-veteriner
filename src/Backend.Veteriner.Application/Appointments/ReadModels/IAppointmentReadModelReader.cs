using Backend.Veteriner.Application.Appointments.Contracts.Dtos;

namespace Backend.Veteriner.Application.Appointments.ReadModels;

public interface IAppointmentReadModelReader
{
    Task<AppointmentListReadResult> GetListAsync(
        AppointmentListReadRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AppointmentCalendarItemDto>> GetCalendarAsync(
        AppointmentCalendarReadRequest request,
        CancellationToken cancellationToken = default);
}
