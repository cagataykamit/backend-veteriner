using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Treatments.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Treatments;
using MediatR;

namespace Backend.Veteriner.Application.Prescriptions.Commands.Create;

public sealed class CreatePrescriptionCommandHandler : IRequestHandler<CreatePrescriptionCommand, Result<Guid>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IReadRepository<Treatment> _treatments;
    private readonly IRepository<Prescription> _prescriptionsWrite;

    public CreatePrescriptionCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Tenant> tenants,
        IReadRepository<Clinic> clinics,
        IReadRepository<Pet> pets,
        IReadRepository<Examination> examinations,
        IReadRepository<Treatment> treatments,
        IRepository<Prescription> prescriptionsWrite)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _tenants = tenants;
        _clinics = clinics;
        _pets = pets;
        _examinations = examinations;
        _treatments = treatments;
        _prescriptionsWrite = prescriptionsWrite;
    }

    public async Task<Result<Guid>> Handle(CreatePrescriptionCommand request, CancellationToken ct)
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
                "Pasif kiracı için reçete kaydı oluşturulamaz.");
        }

        if (_clinicContext.ClinicId.HasValue && request.ClinicId != _clinicContext.ClinicId.Value)
        {
            return Result<Guid>.Failure(
                "Prescriptions.ClinicContextMismatch",
                "Istek clinicId degeri aktif clinic baglami ile uyusmuyor.");
        }

        var effectiveClinicId = _clinicContext.ClinicId ?? request.ClinicId;
        if (effectiveClinicId == Guid.Empty)
            return Result<Guid>.Failure("Prescriptions.Validation", "ClinicId is required.");

        var prescribedUtc = PrescribedAtUtcWindow.ToUtc(request.PrescribedAtUtc);
        var window = PrescribedAtUtcWindow.Validate(prescribedUtc);
        if (!window.IsSuccess)
            return Result<Guid>.Failure(window.Error);

        var followCheck = PrescribedAtUtcWindow.ValidateFollowUpNotBeforePrescription(
            prescribedUtc,
            request.FollowUpDateUtc);
        if (!followCheck.IsSuccess)
            return Result<Guid>.Failure(followCheck.Error);

        var clinic = await _clinics.FirstOrDefaultAsync(
            new ClinicByIdSpec(tenantId, effectiveClinicId), ct);
        if (clinic is null)
            return Result<Guid>.Failure("Clinics.NotFound", "Klinik bulunamadı veya kiracıya ait değil.");

        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, request.PetId), ct);
        if (pet is null)
            return Result<Guid>.Failure("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

        if (request.ExaminationId is { } examId)
        {
            var exam = await _examinations.FirstOrDefaultAsync(new ExaminationByIdSpec(tenantId, examId), ct);
            if (exam is null)
            {
                return Result<Guid>.Failure(
                    "Examinations.NotFound",
                    "Muayene bulunamadı veya kiracıya ait değil.");
            }

            if (exam.ClinicId != effectiveClinicId)
            {
                return Result<Guid>.Failure(
                    "Prescriptions.ExaminationClinicMismatch",
                    "Examination clinic does not match prescription clinic.");
            }

            if (exam.PetId != request.PetId)
            {
                return Result<Guid>.Failure(
                    "Prescriptions.ExaminationPetMismatch",
                    "Examination pet does not match prescription pet.");
            }
        }

        if (request.TreatmentId is { } treatmentId)
        {
            var treatment = await _treatments.FirstOrDefaultAsync(new TreatmentByIdSpec(tenantId, treatmentId), ct);
            if (treatment is null)
            {
                return Result<Guid>.Failure(
                    "Treatments.NotFound",
                    "Treatment not found or does not belong to tenant.");
            }

            if (treatment.ClinicId != effectiveClinicId)
            {
                return Result<Guid>.Failure(
                    "Prescriptions.TreatmentClinicMismatch",
                    "Treatment clinic does not match prescription clinic.");
            }

            if (treatment.PetId != request.PetId)
            {
                return Result<Guid>.Failure(
                    "Prescriptions.TreatmentPetMismatch",
                    "Treatment pet does not match prescription pet.");
            }

            if (request.ExaminationId is { } prescribedExamId)
            {
                if (!treatment.ExaminationId.HasValue || treatment.ExaminationId.Value != prescribedExamId)
                {
                    return Result<Guid>.Failure(
                        "Prescriptions.ExaminationTreatmentMismatch",
                        "When both examination and treatment are set, the treatment must reference the same examination.");
                }
            }
        }

        var prescription = new Prescription(
            tenantId,
            effectiveClinicId,
            request.PetId,
            request.ExaminationId,
            request.TreatmentId,
            prescribedUtc,
            request.Title,
            request.Content,
            request.Notes,
            request.FollowUpDateUtc);

        await _prescriptionsWrite.AddAsync(prescription, ct);
        await _prescriptionsWrite.SaveChangesAsync(ct);
        return Result<Guid>.Success(prescription.Id);
    }
}
