using Backend.Veteriner.Application.Appointments.Access;
using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Visits.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Visits;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Appointments.Commands.NoShow;

public sealed class MarkAppointmentNoShowCommandHandler : IRequestHandler<MarkAppointmentNoShowCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Appointment> _appointmentsRead;
    private readonly IRepository<Appointment> _appointmentsWrite;
    private readonly IReadRepository<Visit> _visits;
    private readonly IAppointmentProjectionSnapshotFactory _snapshotFactory;
    private readonly IAppointmentIntegrationEventOutbox _eventOutbox;
    private readonly TimeProvider _timeProvider;

    public MarkAppointmentNoShowCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Appointment> appointmentsRead,
        IRepository<Appointment> appointmentsWrite,
        IReadRepository<Visit> visits,
        IAppointmentProjectionSnapshotFactory snapshotFactory,
        IAppointmentIntegrationEventOutbox eventOutbox,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _appointmentsRead = appointmentsRead;
        _appointmentsWrite = appointmentsWrite;
        _visits = visits;
        _snapshotFactory = snapshotFactory;
        _eventOutbox = eventOutbox;
        _timeProvider = timeProvider;
    }

    public async Task<Result> Handle(MarkAppointmentNoShowCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var appointment = await _appointmentsRead.FirstOrDefaultAsync(
            new AppointmentByIdSpec(tenantId, request.AppointmentId), ct);

        if (appointment is null
            || (_clinicContext.ClinicId is { } clinicId && appointment.ClinicId != clinicId))
        {
            return Result.Failure("Appointments.NotFound", "Randevu bulunamadı veya kiracıya ait değil.");
        }

        var clinicAccess = await AppointmentClinicWriteScope.EnsureWriteAccessAsync(
            _clinicScopeResolver, tenantId, appointment.ClinicId, ct);
        if (!clinicAccess.IsSuccess)
            return clinicAccess;

        // Hasta gelmişse (yanlış geliş olmayan Visit) gelmedi işaretlenemez; zaten işaretliyse idempotent başarı.
        if (appointment.Status == AppointmentStatus.Scheduled
            && await _visits.AnyAsync(new VisitByAppointmentIdSpec(tenantId, appointment.Id), ct))
        {
            return Result.Failure("Appointments.HasVisit", "Randevu için geliş kaydı var; gelmedi olarak işaretlenemez.");
        }

        var sequenceBefore = appointment.MutationSequence;
        var previous = await _snapshotFactory.CreateAsync(appointment, ct);

        var domain = appointment.MarkNoShow(_timeProvider.GetUtcNow().UtcDateTime, request.Reason);
        if (!domain.IsSuccess)
            return Result.Failure(domain.Error);

        // Zaten gelmedi ise değişiklik/olay yok.
        if (appointment.MutationSequence == sequenceBefore)
            return Result.Success();

        await _eventOutbox.EnqueueAsync(
            AppointmentIntegrationEventTypes.Updated,
            new AppointmentUpdatedIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                appointment.MutationSequence,
                previous,
                _snapshotFactory.CreateScalarsFromPrevious(appointment, previous)),
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
}
