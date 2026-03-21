using Backend.Veteriner.Application.Appointments;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Appointments.Commands.Reschedule;

public sealed class RescheduleAppointmentCommandHandler : IRequestHandler<RescheduleAppointmentCommand, Result>
{
    private readonly IReadRepository<Appointment> _appointmentsRead;
    private readonly IRepository<Appointment> _appointmentsWrite;

    public RescheduleAppointmentCommandHandler(
        IReadRepository<Appointment> appointmentsRead,
        IRepository<Appointment> appointmentsWrite)
    {
        _appointmentsRead = appointmentsRead;
        _appointmentsWrite = appointmentsWrite;
    }

    public async Task<Result> Handle(RescheduleAppointmentCommand request, CancellationToken ct)
    {
        var scheduledUtc = NormalizeToUtc(request.ScheduledAtUtc);

        var appointment = await _appointmentsRead.FirstOrDefaultAsync(
            new AppointmentByIdSpec(request.TenantId, request.AppointmentId), ct);

        if (appointment is null)
            return Result.Failure("Appointments.NotFound", "Randevu bulunamadı veya kiracıya ait değil.");

        if (appointment.Status != AppointmentStatus.Scheduled)
        {
            return Result.Failure(
                "Appointments.InvalidStatusTransition",
                "Yalnızca planlanmış randevu yeniden zamanlanabilir.");
        }

        var window = AppointmentScheduleWindow.Validate(scheduledUtc);
        if (!window.IsSuccess)
            return Result.Failure(window.Error);

        var clinicBusy = await _appointmentsRead.FirstOrDefaultAsync(
            new AppointmentScheduledSlotAtClinicSpec(
                request.TenantId,
                appointment.ClinicId,
                scheduledUtc,
                appointment.Id),
            ct);
        if (clinicBusy is not null)
        {
            return Result.Failure(
                "Appointments.ClinicSlotDuplicate",
                "Bu klinikte aynı saatte başka bir aktif randevu var.");
        }

        var petBusy = await _appointmentsRead.FirstOrDefaultAsync(
            new AppointmentScheduledSlotForPetSpec(
                request.TenantId,
                appointment.PetId,
                scheduledUtc,
                appointment.Id),
            ct);
        if (petBusy is not null)
        {
            return Result.Failure(
                "Appointments.PetSlotDuplicate",
                "Bu hayvanın aynı saatte başka bir aktif randevusu var.");
        }

        var domain = appointment.RescheduleTo(scheduledUtc);
        if (!domain.IsSuccess)
            return Result.Failure(domain.Error);

        await _appointmentsWrite.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static DateTime NormalizeToUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
