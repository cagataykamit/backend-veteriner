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
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Appointment> _appointmentsRead;
    private readonly IRepository<Appointment> _appointmentsWrite;

    public CreateAppointmentCommandHandler(
        IReadRepository<Tenant> tenants,
        IReadRepository<Clinic> clinics,
        IReadRepository<Pet> pets,
        IReadRepository<Appointment> appointmentsRead,
        IRepository<Appointment> appointmentsWrite)
    {
        _tenants = tenants;
        _clinics = clinics;
        _pets = pets;
        _appointmentsRead = appointmentsRead;
        _appointmentsWrite = appointmentsWrite;
    }

    public async Task<Result<Guid>> Handle(CreateAppointmentCommand request, CancellationToken ct)
    {
        var scheduledUtc = NormalizeToUtc(request.ScheduledAtUtc);

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(request.TenantId), ct);
        if (tenant is null)
            return Result<Guid>.Failure("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
            return Result<Guid>.Failure(
                "Tenants.TenantInactive",
                "Pasif kiracı için randevu oluşturulamaz.");

        var window = AppointmentScheduleWindow.Validate(scheduledUtc);
        if (!window.IsSuccess)
            return Result<Guid>.Failure(window.Error);

        var clinic = await _clinics.FirstOrDefaultAsync(
            new ClinicByIdSpec(request.TenantId, request.ClinicId), ct);
        if (clinic is null)
            return Result<Guid>.Failure("Clinics.NotFound", "Klinik bulunamadı veya kiracıya ait değil.");

        var pet = await _pets.FirstOrDefaultAsync(
            new PetByIdSpec(request.TenantId, request.PetId), ct);
        if (pet is null)
            return Result<Guid>.Failure("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

        var clinicBusy = await _appointmentsRead.FirstOrDefaultAsync(
            new AppointmentScheduledSlotAtClinicSpec(request.TenantId, request.ClinicId, scheduledUtc), ct);
        if (clinicBusy is not null)
            return Result<Guid>.Failure(
                "Appointments.ClinicSlotDuplicate",
                "Bu klinikte aynı saatte başka bir aktif randevu var.");

        var petBusy = await _appointmentsRead.FirstOrDefaultAsync(
            new AppointmentScheduledSlotForPetSpec(request.TenantId, request.PetId, scheduledUtc), ct);
        if (petBusy is not null)
            return Result<Guid>.Failure(
                "Appointments.PetSlotDuplicate",
                "Bu hayvanın aynı saatte başka bir aktif randevusu var.");

        var appointment = new Appointment(
            request.TenantId,
            request.ClinicId,
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
}
