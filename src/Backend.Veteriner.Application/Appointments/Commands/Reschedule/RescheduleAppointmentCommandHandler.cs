using Backend.Veteriner.Application.Appointments;
using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.AppointmentSettings;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Appointments.Commands.Reschedule;

public sealed class RescheduleAppointmentCommandHandler : IRequestHandler<RescheduleAppointmentCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Appointment> _appointmentsRead;
    private readonly IReadRepository<ClinicAppointmentSettings> _clinicAppointmentSettings;
    private readonly IReadRepository<ClinicWorkingHour> _clinicWorkingHoursRead;
    private readonly IRepository<Appointment> _appointmentsWrite;
    private readonly IAppointmentProjectionSnapshotFactory _snapshotFactory;
    private readonly IAppointmentIntegrationEventOutbox _eventOutbox;

    public RescheduleAppointmentCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Appointment> appointmentsRead,
        IReadRepository<ClinicAppointmentSettings> clinicAppointmentSettings,
        IReadRepository<ClinicWorkingHour> clinicWorkingHoursRead,
        IRepository<Appointment> appointmentsWrite,
        IAppointmentProjectionSnapshotFactory snapshotFactory,
        IAppointmentIntegrationEventOutbox eventOutbox)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _appointmentsRead = appointmentsRead;
        _clinicAppointmentSettings = clinicAppointmentSettings;
        _clinicWorkingHoursRead = clinicWorkingHoursRead;
        _appointmentsWrite = appointmentsWrite;
        _snapshotFactory = snapshotFactory;
        _eventOutbox = eventOutbox;
    }

    public async Task<Result> Handle(RescheduleAppointmentCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var scheduledUtc = NormalizeToUtc(request.ScheduledAtUtc);

        var appointment = await _appointmentsRead.FirstOrDefaultAsync(
            new AppointmentByIdSpec(tenantId, request.AppointmentId), ct);

        if (appointment is null)
            return Result.Failure("Appointments.NotFound", "Randevu bulunamadı veya kiracıya ait değil.");
        if (_clinicContext.ClinicId is { } clinicId && appointment.ClinicId != clinicId)
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

        var endUtc = scheduledUtc.AddMinutes(appointment.DurationMinutes);
        var appointmentSettingsRow = await _clinicAppointmentSettings.FirstOrDefaultAsync(
            new ClinicAppointmentSettingsByClinicSpec(tenantId, appointment.ClinicId), ct);
        var slotIntervalMinutes = appointmentSettingsRow?.SlotIntervalMinutes
            ?? ClinicAppointmentSettingsDefaults.Build().SlotIntervalMinutes;
        var slotAlignment = AppointmentSlotIntervalValidation.Validate(scheduledUtc, slotIntervalMinutes);
        if (!slotAlignment.IsSuccess)
            return Result.Failure(slotAlignment.Error);

        var allowClinicOverlap = appointmentSettingsRow?.AllowOverlappingAppointments
            ?? ClinicAppointmentSettingsDefaults.Build().AllowOverlappingAppointments;

        if (!allowClinicOverlap)
        {
            var clinicBusy = await _appointmentsRead.FirstOrDefaultAsync(
                new AppointmentOverlappingAtClinicSpec(
                    tenantId,
                    appointment.ClinicId,
                    scheduledUtc,
                    endUtc,
                    appointment.Id),
                ct);
            if (clinicBusy is not null)
            {
                return Result.Failure(
                    "Appointments.ClinicTimeConflict",
                    "Bu zaman aralığında klinikte başka bir planlı randevu var.");
            }
        }

        var petBusy = await _appointmentsRead.FirstOrDefaultAsync(
            new AppointmentOverlappingForPetSpec(
                tenantId,
                appointment.PetId,
                scheduledUtc,
                endUtc,
                appointment.Id),
            ct);
        if (petBusy is not null)
        {
            return Result.Failure(
                "Appointments.PetTimeConflict",
                "Bu zaman aralığında hayvanın başka bir planlı randevusu var.");
        }

        var hoursRows = await _clinicWorkingHoursRead.ListAsync(
            new ClinicWorkingHoursByClinicSpec(tenantId, appointment.ClinicId), ct);
        var workingHours = AppointmentWorkingHoursValidation.Validate(scheduledUtc, appointment.DurationMinutes, hoursRows);
        if (!workingHours.IsSuccess)
            return Result.Failure(workingHours.Error);

        var previous = await _snapshotFactory.CreateAsync(appointment, ct);

        var domain = appointment.RescheduleTo(scheduledUtc);
        if (!domain.IsSuccess)
            return Result.Failure(domain.Error);

        var current = _snapshotFactory.CreateScalarsFromPrevious(appointment, previous);
        await _eventOutbox.EnqueueAsync(
            AppointmentIntegrationEventTypes.Rescheduled,
            new AppointmentRescheduledIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                appointment.MutationSequence,
                previous,
                current),
            ct);

        try
        {
            await _appointmentsWrite.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(
                "Appointments.ConcurrencyConflict",
                "Randevu eşzamanlı olarak güncellendi; işlem tekrarlanmalı.");
        }

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
