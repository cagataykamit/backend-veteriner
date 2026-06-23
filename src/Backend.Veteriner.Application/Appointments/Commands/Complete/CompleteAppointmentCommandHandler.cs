using Backend.Veteriner.Application.Appointments.Access;
using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Appointments.Commands.Complete;

public sealed class CompleteAppointmentCommandHandler : IRequestHandler<CompleteAppointmentCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Appointment> _appointmentsRead;
    private readonly IRepository<Appointment> _appointmentsWrite;
    private readonly IAppointmentProjectionSnapshotFactory _snapshotFactory;
    private readonly IAppointmentIntegrationEventOutbox _eventOutbox;

    public CompleteAppointmentCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Appointment> appointmentsRead,
        IRepository<Appointment> appointmentsWrite,
        IAppointmentProjectionSnapshotFactory snapshotFactory,
        IAppointmentIntegrationEventOutbox eventOutbox)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _appointmentsRead = appointmentsRead;
        _appointmentsWrite = appointmentsWrite;
        _snapshotFactory = snapshotFactory;
        _eventOutbox = eventOutbox;
    }

    public async Task<Result> Handle(CompleteAppointmentCommand request, CancellationToken ct)
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

        var clinicAccess = await AppointmentClinicWriteScope.EnsureWriteAccessAsync(
            _clinicScopeResolver, tenantId, appointment.ClinicId, ct);
        if (!clinicAccess.IsSuccess)
            return clinicAccess;

        var previous = await _snapshotFactory.CreateAsync(appointment, ct);

        var domain = appointment.Complete();
        if (!domain.IsSuccess)
            return Result.Failure(domain.Error);

        var current = _snapshotFactory.CreateScalarsFromPrevious(appointment, previous);
        await _eventOutbox.EnqueueAsync(
            AppointmentIntegrationEventTypes.Completed,
            new AppointmentCompletedIntegrationEvent(
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
