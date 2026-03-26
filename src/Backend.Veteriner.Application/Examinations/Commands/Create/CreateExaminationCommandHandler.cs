using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Examinations.Commands.Create;

public sealed class CreateExaminationCommandHandler : IRequestHandler<CreateExaminationCommand, Result<Guid>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IRepository<Examination> _examinationsWrite;

    public CreateExaminationCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Tenant> tenants,
        IReadRepository<Clinic> clinics,
        IReadRepository<Pet> pets,
        IReadRepository<Appointment> appointments,
        IRepository<Examination> examinationsWrite)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _tenants = tenants;
        _clinics = clinics;
        _pets = pets;
        _appointments = appointments;
        _examinationsWrite = examinationsWrite;
    }

    public async Task<Result<Guid>> Handle(CreateExaminationCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<Guid>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result<Guid>.Failure("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
        {
            return Result<Guid>.Failure(
                "Tenants.TenantInactive",
                "Pasif kiracı için muayene kaydı oluşturulamaz.");
        }

        var examinedUtc = ExaminationExaminedAtWindow.ToUtc(request.ExaminedAtUtc);
        var window = ExaminationExaminedAtWindow.Validate(examinedUtc);

        if (!window.IsSuccess)
            return Result<Guid>.Failure(window.Error);

        Guid clinicId;
        Guid petId;

        Appointment? appt = null;
        if (request.AppointmentId is { } aid)
        {
            appt = await _appointments.FirstOrDefaultAsync(
                new AppointmentByIdSpec(tenantId, aid), ct);
            if (appt is null)
            {
                return Result<Guid>.Failure(
                    "Appointments.NotFound",
                    "Randevu bulunamadı veya kiracıya ait değil.");
            }

            if (request.ClinicId.HasValue && _clinicContext.ClinicId.HasValue && request.ClinicId.Value != _clinicContext.ClinicId.Value)
            {
                return Result<Guid>.Failure(
                    "Examinations.ClinicContextMismatch",
                    "Istek clinicId degeri aktif clinic baglami ile uyusmuyor.");
            }

            clinicId = _clinicContext.ClinicId ?? request.ClinicId ?? appt.ClinicId;
            petId = request.PetId ?? appt.PetId;

            if (clinicId != appt.ClinicId || petId != appt.PetId)
            {
                return Result<Guid>.Failure(
                    "Examinations.AppointmentPetClinicMismatch",
                    "Seçilen randevu ile klinik veya hayvan bilgisi uyuşmuyor.");
            }
        }
        else
        {
            var cid = _clinicContext.ClinicId ?? request.ClinicId;
            if (cid is not { } resolvedCid || resolvedCid == Guid.Empty
                || request.PetId is not { } pid || pid == Guid.Empty)
            {
                return Result<Guid>.Failure(
                    "Examinations.Validation",
                    "AppointmentId yoksa ClinicId ve PetId zorunludur.");
            }

            clinicId = resolvedCid;
            petId = pid;
        }

        var clinic = await _clinics.FirstOrDefaultAsync(
            new ClinicByIdSpec(tenantId, clinicId), ct);
        if (clinic is null)
            return Result<Guid>.Failure("Clinics.NotFound", "Klinik bulunamadı veya kiracıya ait değil.");

        var pet = await _pets.FirstOrDefaultAsync(
            new PetByIdSpec(tenantId, petId), ct);
        if (pet is null)
            return Result<Guid>.Failure("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

        var examination = new Examination(
            tenantId,
            clinicId,
            petId,
            request.AppointmentId,
            examinedUtc,
            request.VisitReason,
            request.Findings,
            request.Assessment,
            request.Notes);

        await _examinationsWrite.AddAsync(examination, ct);
        await _examinationsWrite.SaveChangesAsync(ct);
        return Result<Guid>.Success(examination.Id);
    }
}
