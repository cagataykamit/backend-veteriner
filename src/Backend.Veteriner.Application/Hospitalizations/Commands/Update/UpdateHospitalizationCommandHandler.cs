using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Hospitalizations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Hospitalizations.Commands.Update;

public sealed class UpdateHospitalizationCommandHandler : IRequestHandler<UpdateHospitalizationCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IReadRepository<Hospitalization> _hospitalizationsRead;
    private readonly IRepository<Hospitalization> _hospitalizationsWrite;

    public UpdateHospitalizationCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Tenant> tenants,
        IReadRepository<Clinic> clinics,
        IReadRepository<Pet> pets,
        IReadRepository<Examination> examinations,
        IReadRepository<Hospitalization> hospitalizationsRead,
        IRepository<Hospitalization> hospitalizationsWrite)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _tenants = tenants;
        _clinics = clinics;
        _pets = pets;
        _examinations = examinations;
        _hospitalizationsRead = hospitalizationsRead;
        _hospitalizationsWrite = hospitalizationsWrite;
    }

    public async Task<Result> Handle(UpdateHospitalizationCommand request, CancellationToken ct)
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
                "Pasif kiracı için yatış kaydı güncellenemez.");
        }

        var existing = await _hospitalizationsRead.FirstOrDefaultAsync(
            new HospitalizationByIdSpec(tenantId, request.Id), ct);
        if (existing is null)
            return Result.Failure("Hospitalizations.NotFound", "Yatış kaydı bulunamadı.");

        if (_clinicContext.ClinicId.HasValue && request.ClinicId != _clinicContext.ClinicId.Value)
        {
            return Result.Failure(
                "Hospitalizations.ClinicContextMismatch",
                "Istek clinicId degeri aktif clinic baglami ile uyusmuyor.");
        }

        var effectiveClinicId = _clinicContext.ClinicId ?? request.ClinicId;
        if (effectiveClinicId == Guid.Empty)
            return Result.Failure("Hospitalizations.Validation", "ClinicId is required.");

        var admittedUtc = AdmittedAtUtcWindow.ToUtc(request.AdmittedAtUtc);
        var window = AdmittedAtUtcWindow.Validate(admittedUtc);
        if (!window.IsSuccess)
            return window;

        if (request.PlannedDischargeAtUtc.HasValue
            && AdmittedAtUtcWindow.ToUtc(request.PlannedDischargeAtUtc.Value) < admittedUtc)
        {
            return Result.Failure(
                "Hospitalizations.PlannedDischargeBeforeAdmission",
                "Planned discharge must not be before admission.");
        }

        var clinic = await _clinics.FirstOrDefaultAsync(
            new ClinicByIdSpec(tenantId, effectiveClinicId), ct);
        if (clinic is null)
            return Result.Failure("Clinics.NotFound", "Klinik bulunamadı veya kiracıya ait değil.");

        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, request.PetId), ct);
        if (pet is null)
            return Result.Failure("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

        if (request.ExaminationId is { } examId)
        {
            var exam = await _examinations.FirstOrDefaultAsync(new ExaminationByIdSpec(tenantId, examId), ct);
            if (exam is null)
            {
                return Result.Failure(
                    "Examinations.NotFound",
                    "Muayene bulunamadı veya kiracıya ait değil.");
            }

            if (exam.ClinicId != effectiveClinicId)
            {
                return Result.Failure(
                    "Hospitalizations.ExaminationClinicMismatch",
                    "Examination clinic does not match hospitalization clinic.");
            }

            if (exam.PetId != request.PetId)
            {
                return Result.Failure(
                    "Hospitalizations.ExaminationPetMismatch",
                    "Examination pet does not match hospitalization pet.");
            }
        }

        var otherActive = await _hospitalizationsRead.AnyAsync(
            new ActiveHospitalizationForPetClinicSpec(tenantId, effectiveClinicId, request.PetId, request.Id),
            ct);
        if (otherActive)
        {
            return Result.Failure(
                "Hospitalizations.ActiveHospitalizationExists",
                "An active hospitalization already exists for this pet at this clinic.");
        }

        var update = existing.UpdateDetails(
            effectiveClinicId,
            request.PetId,
            request.ExaminationId,
            admittedUtc,
            request.PlannedDischargeAtUtc,
            request.Reason,
            request.Notes);

        if (!update.IsSuccess)
            return update;

        await _hospitalizationsWrite.UpdateAsync(existing, ct);
        await _hospitalizationsWrite.SaveChangesAsync(ct);
        return Result.Success();
    }
}
