using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Examinations.Commands.Update;

public sealed class UpdateExaminationCommandHandler : IRequestHandler<UpdateExaminationCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IReadRepository<Examination> _examinationsRead;
    private readonly IRepository<Examination> _examinationsWrite;

    public UpdateExaminationCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Tenant> tenants,
        IReadRepository<Clinic> clinics,
        IReadRepository<Pet> pets,
        IReadRepository<Appointment> appointments,
        IReadRepository<Examination> examinationsRead,
        IRepository<Examination> examinationsWrite)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _tenants = tenants;
        _clinics = clinics;
        _pets = pets;
        _appointments = appointments;
        _examinationsRead = examinationsRead;
        _examinationsWrite = examinationsWrite;
    }

    public async Task<Result> Handle(UpdateExaminationCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result.Failure("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
        {
            return Result.Failure(
                "Tenants.TenantInactive",
                "Pasif kiracı için muayene kaydı güncellenemez.");
        }

        var e = await _examinationsRead.FirstOrDefaultAsync(new ExaminationByIdSpec(tenantId, request.Id), ct);
        if (e is null)
            return Result.Failure("Examinations.NotFound", "Muayene kaydı bulunamadı.");

        var examinedUtc = ExaminationExaminedAtWindow.ToUtc(request.ExaminedAtUtc);
        var window = ExaminationExaminedAtWindow.Validate(examinedUtc);
        if (!window.IsSuccess)
            return Result.Failure(window.Error);

        Guid clinicId;
        Guid petId;
        Guid? appointmentId;

        Appointment? appt = null;
        if (request.AppointmentId is { } aid)
        {
            appt = await _appointments.FirstOrDefaultAsync(new AppointmentByIdSpec(tenantId, aid), ct);
            if (appt is null)
            {
                return Result.Failure(
                    "Appointments.NotFound",
                    "Randevu bulunamadı veya kiracıya ait değil.");
            }

            if (request.ClinicId.HasValue && _clinicContext.ClinicId.HasValue && request.ClinicId.Value != _clinicContext.ClinicId.Value)
            {
                return Result.Failure(
                    "Examinations.ClinicContextMismatch",
                    "Istek clinicId degeri aktif clinic baglami ile uyusmuyor.");
            }

            clinicId = _clinicContext.ClinicId ?? request.ClinicId ?? appt.ClinicId;
            petId = request.PetId ?? appt.PetId;
            appointmentId = aid;

            if (clinicId != appt.ClinicId || petId != appt.PetId)
            {
                return Result.Failure(
                    "Examinations.AppointmentPetClinicMismatch",
                    "Seçilen randevu ile klinik veya hayvan bilgisi uyuşmuyor.");
            }
        }
        else
        {
            var cid = _clinicContext.ClinicId ?? request.ClinicId ?? e.ClinicId;
            var pid = request.PetId ?? e.PetId;

            if (cid == Guid.Empty || pid == Guid.Empty)
                return Result.Failure("Examinations.Validation", "ClinicId ve PetId zorunludur.");

            clinicId = cid;
            petId = pid;
            appointmentId = null;
        }

        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, clinicId), ct);
        if (clinic is null)
            return Result.Failure("Clinics.NotFound", "Klinik bulunamadı veya kiracıya ait değil.");

        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, petId), ct);
        if (pet is null)
            return Result.Failure("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

        var domain = e.UpdateDetails(
            clinicId,
            petId,
            appointmentId,
            examinedUtc,
            request.VisitReason,
            request.Findings,
            request.Assessment,
            request.Notes);

        if (!domain.IsSuccess)
            return domain;

        await _examinationsWrite.UpdateAsync(e, ct);
        await _examinationsWrite.SaveChangesAsync(ct);
        return Result.Success();
    }
}

