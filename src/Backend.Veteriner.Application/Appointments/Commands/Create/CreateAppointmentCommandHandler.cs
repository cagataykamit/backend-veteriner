using Backend.Veteriner.Application.Appointments;
using Backend.Veteriner.Application.Appointments.Access;
using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.AppointmentSettings;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Appointments.Commands.Create;

public sealed class CreateAppointmentCommandHandler : IRequestHandler<CreateAppointmentCommand, Result<Guid>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Appointment> _appointmentsRead;
    private readonly IReadRepository<ClinicAppointmentSettings> _clinicAppointmentSettings;
    private readonly IReadRepository<ClinicWorkingHour> _clinicWorkingHoursRead;
    private readonly IRepository<Appointment> _appointmentsWrite;
    private readonly IAppointmentProjectionSnapshotFactory _snapshotFactory;
    private readonly IAppointmentIntegrationEventOutbox _eventOutbox;
    private readonly AppointmentAssignment _assignment;
    private readonly TimeProvider _timeProvider;

    public CreateAppointmentCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IReadRepository<Clinic> clinics,
        IReadRepository<Pet> pets,
        IReadRepository<Appointment> appointmentsRead,
        IReadRepository<ClinicAppointmentSettings> clinicAppointmentSettings,
        IReadRepository<ClinicWorkingHour> clinicWorkingHoursRead,
        IRepository<Appointment> appointmentsWrite,
        IAppointmentProjectionSnapshotFactory snapshotFactory,
        IAppointmentIntegrationEventOutbox eventOutbox,
        AppointmentAssignment assignment,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _clinics = clinics;
        _pets = pets;
        _appointmentsRead = appointmentsRead;
        _clinicAppointmentSettings = clinicAppointmentSettings;
        _clinicWorkingHoursRead = clinicWorkingHoursRead;
        _appointmentsWrite = appointmentsWrite;
        _snapshotFactory = snapshotFactory;
        _eventOutbox = eventOutbox;
        _assignment = assignment;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<Guid>> Handle(CreateAppointmentCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<Guid>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var scheduledUtc = NormalizeToUtc(request.ScheduledAtUtc);
        var effectiveStatus = request.Status ?? AppointmentStatus.Scheduled;
        if (!Enum.IsDefined(effectiveStatus))
            return Result<Guid>.Failure("Appointments.Validation", "Randevu durumu geçersiz.");

        // Tamamlama/iptal/gelmedi kendi izinli uçlarıyla yapılır; Create ile başka durumda randevu açılamaz.
        if (effectiveStatus != AppointmentStatus.Scheduled)
        {
            return Result<Guid>.Failure(
                "Appointments.Validation",
                "Yeni randevu yalnızca Planlanmış (Scheduled) durumda oluşturulabilir.");
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result<Guid>.Failure("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
            return Result<Guid>.Failure(
                "Tenants.TenantInactive",
                "Pasif kiracı için randevu oluşturulamaz.");

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        if (effectiveStatus == AppointmentStatus.Scheduled)
        {
            var window = AppointmentScheduleWindow.Validate(scheduledUtc, utcNow);
            if (!window.IsSuccess)
                return Result<Guid>.Failure(window.Error);
        }

        if (ClinicContextRule.Conflicts(request.ClinicId, _clinicContext.ClinicId))
            return Result<Guid>.Failure(ClinicContextRule.Mismatch("Appointments"));

        var effectiveClinicId = _clinicContext.ClinicId ?? request.ClinicId;
        var clinicResolve = await ResolveClinicAsync(tenantId, effectiveClinicId, ct);
        if (!clinicResolve.IsSuccess)
            return Result<Guid>.Failure(clinicResolve.Error);
        var clinic = clinicResolve.Value!;

        var clinicId = clinic.Id;

        var clinicAccess = await AppointmentClinicWriteScope.EnsureWriteAccessAsync(
            _clinicScopeResolver, tenantId, clinicId, ct);
        if (!clinicAccess.IsSuccess)
            return Result<Guid>.Failure(clinicAccess.Error);

        var pet = await _pets.FirstOrDefaultAsync(
            new PetByIdSpec(tenantId, request.PetId), ct);
        if (pet is null)
            return Result<Guid>.Failure("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

        var careGate = pet.EnsureCanStartCare();
        if (!careGate.IsSuccess)
            return Result<Guid>.Failure(careGate.Error);

        var appointmentSettingsRow = await _clinicAppointmentSettings.FirstOrDefaultAsync(
            new ClinicAppointmentSettingsByClinicSpec(tenantId, clinicId), ct);

        var reasonResult = await _assignment.ResolveReasonAsync(tenantId, clinicId, request.ReasonId, null, ct);
        if (!reasonResult.IsSuccess)
            return Result<Guid>.Failure(reasonResult.Error);

        // Süre önceliği: istekteki süre → nedenin varsayılan süresi → klinik varsayılanı (ADR-042 K2).
        int durationMinutes;
        if (request.DurationMinutes is { } requestedDuration)
            durationMinutes = requestedDuration;
        else
            durationMinutes = reasonResult.Value?.DefaultDurationMinutes
                ?? appointmentSettingsRow?.DefaultAppointmentDurationMinutes
                ?? ClinicAppointmentSettingsDefaults.Build().DefaultAppointmentDurationMinutes;

        if (!Appointment.IsValidDurationMinutes(durationMinutes))
        {
            return Result<Guid>.Failure(
                "Appointments.Validation",
                "Randevu süresi 5-240 dakika arasında olmalıdır.");
        }

        if (effectiveStatus == AppointmentStatus.Scheduled)
        {
            // Slot, çakışma ve çalışma saati kuralları tek yerde (bekleme listesi kabulü de kullanır).
            var bookable = await AppointmentSlotRules.EnsureBookableAsync(
                _appointmentsRead,
                _clinicWorkingHoursRead,
                _assignment,
                appointmentSettingsRow,
                tenantId,
                clinicId,
                request.PetId,
                request.VeterinarianUserId,
                scheduledUtc,
                durationMinutes,
                ct);
            if (!bookable.IsSuccess)
                return Result<Guid>.Failure(bookable.Error);
        }

        var appointment = new Appointment(
            tenantId,
            clinicId,
            request.PetId,
            scheduledUtc,
            durationMinutes,
            request.AppointmentType,
            request.Status,
            request.Notes,
            request.ReasonId,
            request.VeterinarianUserId);

        await _appointmentsWrite.AddAsync(appointment, ct);
        await _assignment.NotifyIfAssignedAsync(appointment, null, ct);

        var sequence = appointment.AdvanceMutationSequence();
        var current = await _snapshotFactory.CreateAsync(appointment, ct);
        await _eventOutbox.EnqueueAsync(
            AppointmentIntegrationEventTypes.Created,
            new AppointmentCreatedIntegrationEvent(Guid.NewGuid(), utcNow, sequence, current),
            ct);

        try
        {
            await _appointmentsWrite.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<Guid>.Failure(
                "Appointments.ConcurrencyConflict",
                "Randevu eşzamanlı olarak güncellendi; işlem tekrarlanmalı.");
        }

        return Result<Guid>.Success(appointment.Id);
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

    private async Task<Result<Clinic>> ResolveClinicAsync(Guid tenantId, Guid? requestedClinicId, CancellationToken ct)
    {
        if (requestedClinicId is { } clinicId)
        {
            var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, clinicId), ct);
            if (clinic is null)
                return Result<Clinic>.Failure("Clinics.NotFound", "Klinik bulunamadı veya kiracıya ait değil.");
            if (!clinic.IsActive)
                return Result<Clinic>.Failure("Clinics.Inactive", "Seçilen klinik pasif.");
            return Result<Clinic>.Success(clinic);
        }

        var activeClinics = await _clinics.ListAsync(new ActiveClinicsByTenantTakeSpec(tenantId, 2), ct);
        if (activeClinics.Count == 0)
            return Result<Clinic>.Failure("Clinics.NotFound", "Randevu için aktif klinik bulunamadı.");
        if (activeClinics.Count > 1)
            return Result<Clinic>.Failure(
                "Clinics.ClinicSelectionRequired",
                "Birden fazla aktif klinik var; clinicId gönderilmelidir.");

        return Result<Clinic>.Success(activeClinics[0]);
    }
}
