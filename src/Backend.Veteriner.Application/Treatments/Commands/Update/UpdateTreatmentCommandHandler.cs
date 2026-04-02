using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Treatments.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Treatments;
using MediatR;

namespace Backend.Veteriner.Application.Treatments.Commands.Update;

public sealed class UpdateTreatmentCommandHandler : IRequestHandler<UpdateTreatmentCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IReadRepository<Treatment> _treatmentsRead;
    private readonly IRepository<Treatment> _treatmentsWrite;

    public UpdateTreatmentCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Tenant> tenants,
        IReadRepository<Clinic> clinics,
        IReadRepository<Pet> pets,
        IReadRepository<Examination> examinations,
        IReadRepository<Treatment> treatmentsRead,
        IRepository<Treatment> treatmentsWrite)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _tenants = tenants;
        _clinics = clinics;
        _pets = pets;
        _examinations = examinations;
        _treatmentsRead = treatmentsRead;
        _treatmentsWrite = treatmentsWrite;
    }

    public async Task<Result> Handle(UpdateTreatmentCommand request, CancellationToken ct)
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
                "Pasif kiracı için tedavi kaydı güncellenemez.");
        }

        var t = await _treatmentsRead.FirstOrDefaultAsync(new TreatmentByIdSpec(tenantId, request.Id), ct);
        if (t is null)
            return Result.Failure("Treatments.NotFound", "Tedavi kaydı bulunamadı.");

        if (_clinicContext.ClinicId.HasValue && request.ClinicId != _clinicContext.ClinicId.Value)
        {
            return Result.Failure(
                "Treatments.ClinicContextMismatch",
                "Istek clinicId degeri aktif clinic baglami ile uyusmuyor.");
        }

        var effectiveClinicId = _clinicContext.ClinicId ?? request.ClinicId;
        if (effectiveClinicId == Guid.Empty)
            return Result.Failure("Treatments.Validation", "ClinicId is required.");

        var treatmentUtc = TreatmentDateUtcWindow.ToUtc(request.TreatmentDateUtc);
        var window = TreatmentDateUtcWindow.Validate(treatmentUtc);
        if (!window.IsSuccess)
            return window;

        var followCheck = TreatmentDateUtcWindow.ValidateFollowUpNotBeforeTreatment(
            treatmentUtc,
            request.FollowUpDateUtc);
        if (!followCheck.IsSuccess)
            return followCheck;

        var clinic = await _clinics.FirstOrDefaultAsync(
            new ClinicByIdSpec(tenantId, effectiveClinicId), ct);
        if (clinic is null)
            return Result.Failure("Clinics.NotFound", "Klinik bulunamadı veya kiracıya ait değil.");

        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, request.PetId), ct);
        if (pet is null)
            return Result.Failure("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

        if (request.ExaminationId is { } eid)
        {
            var exam = await _examinations.FirstOrDefaultAsync(new ExaminationByIdSpec(tenantId, eid), ct);
            if (exam is null)
            {
                return Result.Failure(
                    "Examinations.NotFound",
                    "Muayene bulunamadı veya kiracıya ait değil.");
            }

            if (exam.ClinicId != effectiveClinicId)
            {
                return Result.Failure(
                    "Treatments.ExaminationClinicMismatch",
                    "Examination clinic does not match treatment clinic.");
            }

            if (exam.PetId != request.PetId)
            {
                return Result.Failure(
                    "Treatments.ExaminationPetMismatch",
                    "Examination pet does not match treatment pet.");
            }
        }

        var update = t.UpdateDetails(
            effectiveClinicId,
            request.PetId,
            request.ExaminationId,
            treatmentUtc,
            request.Title,
            request.Description,
            request.Notes,
            request.FollowUpDateUtc);

        if (!update.IsSuccess)
            return update;

        await _treatmentsWrite.UpdateAsync(t, ct);
        await _treatmentsWrite.SaveChangesAsync(ct);
        return Result.Success();
    }
}
