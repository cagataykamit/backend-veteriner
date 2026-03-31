using Backend.Veteriner.Application.Appointments;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Appointments.Commands.Update;

public sealed class UpdateAppointmentCommandHandler : IRequestHandler<UpdateAppointmentCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Appointment> _appointmentsRead;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Pet> _pets;
    private readonly IRepository<Appointment> _appointmentsWrite;

    public UpdateAppointmentCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Appointment> appointmentsRead,
        IReadRepository<Clinic> clinics,
        IReadRepository<Pet> pets,
        IRepository<Appointment> appointmentsWrite)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _appointmentsRead = appointmentsRead;
        _clinics = clinics;
        _pets = pets;
        _appointmentsWrite = appointmentsWrite;
    }

    public async Task<Result> Handle(UpdateAppointmentCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result.Failure(
                "Tenants.ContextMissing",
                "Kiracý baðlamý yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var appointment = await _appointmentsRead.FirstOrDefaultAsync(new AppointmentByIdSpec(tenantId, request.Id), ct);
        if (appointment is null)
            return Result.Failure("Appointments.NotFound", "Randevu bulunamadý veya kiracýya ait deðil.");

        var scheduledUtc = NormalizeToUtc(request.ScheduledAtUtc);
        if (request.Status == AppointmentStatus.Scheduled)
        {
            var window = AppointmentScheduleWindow.Validate(scheduledUtc);
            if (!window.IsSuccess)
                return Result.Failure(window.Error);
        }

        if (request.ClinicId.HasValue && _clinicContext.ClinicId.HasValue && request.ClinicId.Value != _clinicContext.ClinicId.Value)
            return Result.Failure("Appointments.ClinicContextMismatch", "Istek clinicId degeri aktif clinic baglami ile uyusmuyor.");

        var clinicId = _clinicContext.ClinicId ?? request.ClinicId ?? appointment.ClinicId;
        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, clinicId), ct);
        if (clinic is null)
            return Result.Failure("Clinics.NotFound", "Klinik bulunamadý veya kiracýya ait deðil.");
        if (!clinic.IsActive)
            return Result.Failure("Clinics.Inactive", "Seçilen klinik pasif.");
        if (_clinicContext.ClinicId is { } currentClinicId && clinicId != currentClinicId)
            return Result.Failure("Appointments.ClinicContextMismatch", "Randevu sadece aktif clinic baglaminda guncellenebilir.");

        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, request.PetId), ct);
        if (pet is null)
            return Result.Failure("Pets.NotFound", "Hayvan kaydý bulunamadý veya kiracýya ait deðil.");

        if (request.Status == AppointmentStatus.Scheduled)
        {
            var clinicBusy = await _appointmentsRead.FirstOrDefaultAsync(
                new AppointmentScheduledSlotAtClinicSpec(tenantId, clinicId, scheduledUtc, appointment.Id), ct);
            if (clinicBusy is not null)
                return Result.Failure("Appointments.ClinicSlotDuplicate", "Bu klinikte ayný saatte baþka bir aktif randevu var.");

            var petBusy = await _appointmentsRead.FirstOrDefaultAsync(
                new AppointmentScheduledSlotForPetSpec(tenantId, request.PetId, scheduledUtc, appointment.Id), ct);
            if (petBusy is not null)
                return Result.Failure("Appointments.PetSlotDuplicate", "Bu hayvanýn ayný saatte baþka bir aktif randevusu var.");
        }

        var domain = appointment.ApplyWriteUpdate(
            request.Status,
            clinicId,
            request.PetId,
            scheduledUtc,
            request.AppointmentType,
            request.Notes);
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