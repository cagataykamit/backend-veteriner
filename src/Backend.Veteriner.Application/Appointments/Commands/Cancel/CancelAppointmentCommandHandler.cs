using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Appointments.Commands.Cancel;

public sealed class CancelAppointmentCommandHandler : IRequestHandler<CancelAppointmentCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Appointment> _appointmentsRead;
    private readonly IRepository<Appointment> _appointmentsWrite;
    private readonly IAppointmentProjectionSnapshotFactory _snapshotFactory;
    private readonly IAppointmentIntegrationEventOutbox _eventOutbox;

    public CancelAppointmentCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Appointment> appointmentsRead,
        IRepository<Appointment> appointmentsWrite,
        IAppointmentProjectionSnapshotFactory snapshotFactory,
        IAppointmentIntegrationEventOutbox eventOutbox)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _appointmentsRead = appointmentsRead;
        _appointmentsWrite = appointmentsWrite;
        _snapshotFactory = snapshotFactory;
        _eventOutbox = eventOutbox;
    }

    public async Task<Result> Handle(CancelAppointmentCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var appointment = await _appointmentsRead.FirstOrDefaultAsync(
            new AppointmentByIdSpec(tenantId, request.AppointmentId), ct);

        if (appointment is null)
            return Result.Failure("Appointments.NotFound", "Randevu bulunamadı veya kiracıya ait değil.");
        if (_clinicContext.ClinicId is { } clinicId && appointment.ClinicId != clinicId)
            return Result.Failure("Appointments.NotFound", "Randevu bulunamadı veya kiracıya ait değil.");

        var previous = await _snapshotFactory.CreateAsync(appointment, ct);

        var domain = appointment.Cancel(request.Reason);
        if (!domain.IsSuccess)
            return Result.Failure(domain.Error);

        var current = _snapshotFactory.CreateScalarsFromPrevious(appointment, previous);
        await _eventOutbox.EnqueueAsync(
            AppointmentIntegrationEventTypes.Cancelled,
            new AppointmentCancelledIntegrationEvent(
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
}
