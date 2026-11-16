using Backend.Veteriner.Application.Appointments.Access;
using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.AppointmentSettings;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Waitlist;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Waitlist;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Appointments.Commands.RevertCancellation;

/// <summary>
/// İptali geri alır. Sıra: süre ve durum (domain) → boşalan saat bekleme listesinde teklif edildiyse engel → bu arada saate giren
/// randevuyla çakışma. Hatırlatmalar ayrıca bir şey gerektirmez: işleyici adayları her turda planlı randevulardan seçer.
/// </summary>
public sealed class RevertAppointmentCancellationCommandHandler : IRequestHandler<RevertAppointmentCancellationCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Appointment> _appointmentsRead;
    private readonly IRepository<Appointment> _appointmentsWrite;
    private readonly IReadRepository<WaitlistOffer> _offers;
    private readonly IReadRepository<ClinicAppointmentSettings> _settings;
    private readonly AppointmentAssignment _assignment;
    private readonly IAppointmentProjectionSnapshotFactory _snapshotFactory;
    private readonly IAppointmentIntegrationEventOutbox _eventOutbox;
    private readonly TimeProvider _timeProvider;

    public RevertAppointmentCancellationCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Appointment> appointmentsRead,
        IRepository<Appointment> appointmentsWrite,
        IReadRepository<WaitlistOffer> offers,
        IReadRepository<ClinicAppointmentSettings> settings,
        AppointmentAssignment assignment,
        IAppointmentProjectionSnapshotFactory snapshotFactory,
        IAppointmentIntegrationEventOutbox eventOutbox,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _appointmentsRead = appointmentsRead;
        _appointmentsWrite = appointmentsWrite;
        _offers = offers;
        _settings = settings;
        _assignment = assignment;
        _snapshotFactory = snapshotFactory;
        _eventOutbox = eventOutbox;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(RevertAppointmentCancellationCommand request, CancellationToken ct)
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

        var previous = await _snapshotFactory.CreateAsync(appointment, ct);

        var domain = appointment.RevertCancellation(_timeProvider.GetUtcNow().UtcDateTime);
        if (!domain.IsSuccess)
            return Result.Failure(domain.Error);

        var offers = await _offers.ListAsync(new WaitlistOffersBySourceSpec(tenantId, appointment.Id), ct);
        if (offers.Any(o => o.Status is WaitlistOfferStatus.Open or WaitlistOfferStatus.Accepted or WaitlistOfferStatus.SlotTaken))
        {
            return Result.Failure(
                "Appointments.CancellationUndoBlocked",
                "Boşalan saat bekleme listesinden bir müşteriye teklif edildi; iptal geri alınamaz.");
        }

        var settingsRow = await _settings.FirstOrDefaultAsync(
            new ClinicAppointmentSettingsByClinicSpec(tenantId, appointment.ClinicId), ct);
        await _assignment.LockScheduleAsync(tenantId, ct);
        var conflicts = await AppointmentSlotRules.EnsureNoConflictAsync(
            _appointmentsRead,
            _assignment,
            settingsRow?.AllowOverlappingAppointments ?? ClinicAppointmentSettingsDefaults.Build().AllowOverlappingAppointments,
            tenantId,
            appointment.ClinicId,
            appointment.PetId,
            appointment.VeterinarianUserId,
            appointment.VeterinarianUserId,
            appointment.ScheduledAtUtc,
            appointment.ScheduledEndUtc,
            appointment.Id,
            ct);
        if (!conflicts.IsSuccess)
            return conflicts;

        await _eventOutbox.EnqueueAsync(
            AppointmentIntegrationEventTypes.Updated,
            new AppointmentUpdatedIntegrationEvent(
                Guid.NewGuid(),
                _timeProvider.GetUtcNow().UtcDateTime,
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
