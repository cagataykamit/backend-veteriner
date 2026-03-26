using Backend.Veteriner.Application.Appointments;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Appointments.Commands.Create;

public sealed class CreateAppointmentCommandHandler : IRequestHandler<CreateAppointmentCommand, Result<Guid>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Appointment> _appointmentsRead;
    private readonly IRepository<Appointment> _appointmentsWrite;

    public CreateAppointmentCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Tenant> tenants,
        IReadRepository<Clinic> clinics,
        IReadRepository<Pet> pets,
        IReadRepository<Appointment> appointmentsRead,
        IRepository<Appointment> appointmentsWrite)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _tenants = tenants;
        _clinics = clinics;
        _pets = pets;
        _appointmentsRead = appointmentsRead;
        _appointmentsWrite = appointmentsWrite;
    }

    public async Task<Result<Guid>> Handle(CreateAppointmentCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<Guid>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var scheduledUtc = NormalizeToUtc(request.ScheduledAtUtc);

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result<Guid>.Failure("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
            return Result<Guid>.Failure(
                "Tenants.TenantInactive",
                "Pasif kiracı için randevu oluşturulamaz.");

        var window = AppointmentScheduleWindow.Validate(scheduledUtc);
        if (!window.IsSuccess)
            return Result<Guid>.Failure(window.Error);

        if (request.ClinicId.HasValue && _clinicContext.ClinicId.HasValue && request.ClinicId.Value != _clinicContext.ClinicId.Value)
            return Result<Guid>.Failure("Appointments.ClinicContextMismatch", "Istek clinicId degeri aktif clinic baglami ile uyusmuyor.");

        var effectiveClinicId = _clinicContext.ClinicId ?? request.ClinicId;
        var clinicResolve = await ResolveClinicAsync(tenantId, effectiveClinicId, ct);
        if (!clinicResolve.IsSuccess)
            return Result<Guid>.Failure(clinicResolve.Error);
        var clinic = clinicResolve.Value!;

        var clinicId = clinic.Id;

        var pet = await _pets.FirstOrDefaultAsync(
            new PetByIdSpec(tenantId, request.PetId), ct);
        if (pet is null)
            return Result<Guid>.Failure("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

        var clinicBusy = await _appointmentsRead.FirstOrDefaultAsync(
            new AppointmentScheduledSlotAtClinicSpec(tenantId, clinicId, scheduledUtc), ct);
        if (clinicBusy is not null)
            return Result<Guid>.Failure(
                "Appointments.ClinicSlotDuplicate",
                "Bu klinikte aynı saatte başka bir aktif randevu var.");

        var petBusy = await _appointmentsRead.FirstOrDefaultAsync(
            new AppointmentScheduledSlotForPetSpec(tenantId, request.PetId, scheduledUtc), ct);
        if (petBusy is not null)
            return Result<Guid>.Failure(
                "Appointments.PetSlotDuplicate",
                "Bu hayvanın aynı saatte başka bir aktif randevusu var.");

        var appointment = new Appointment(
            tenantId,
            clinicId,
            request.PetId,
            scheduledUtc,
            request.Notes);

        await _appointmentsWrite.AddAsync(appointment, ct);
        await _appointmentsWrite.SaveChangesAsync(ct);
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
