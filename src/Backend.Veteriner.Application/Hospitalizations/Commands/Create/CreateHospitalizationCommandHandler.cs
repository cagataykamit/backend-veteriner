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

namespace Backend.Veteriner.Application.Hospitalizations.Commands.Create;

public sealed class CreateHospitalizationCommandHandler : IRequestHandler<CreateHospitalizationCommand, Result<Guid>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IReadRepository<Hospitalization> _hospitalizationsRead;
    private readonly IRepository<Hospitalization> _hospitalizationsWrite;

    public CreateHospitalizationCommandHandler(
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

    public async Task<Result<Guid>> Handle(CreateHospitalizationCommand request, CancellationToken ct)
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
                "Pasif kiracı için yatış kaydı oluşturulamaz.");
        }

        if (_clinicContext.ClinicId.HasValue && request.ClinicId != _clinicContext.ClinicId.Value)
        {
            return Result<Guid>.Failure(
                "Hospitalizations.ClinicContextMismatch",
                "Istek clinicId degeri aktif clinic baglami ile uyusmuyor.");
        }

        var effectiveClinicId = _clinicContext.ClinicId ?? request.ClinicId;
        if (effectiveClinicId == Guid.Empty)
            return Result<Guid>.Failure("Hospitalizations.Validation", "ClinicId is required.");

        var admittedUtc = AdmittedAtUtcWindow.ToUtc(request.AdmittedAtUtc);
        var window = AdmittedAtUtcWindow.Validate(admittedUtc);
        if (!window.IsSuccess)
            return Result<Guid>.Failure(window.Error);

        if (request.PlannedDischargeAtUtc.HasValue
            && AdmittedAtUtcWindow.ToUtc(request.PlannedDischargeAtUtc.Value) < admittedUtc)
        {
            return Result<Guid>.Failure(
                "Hospitalizations.PlannedDischargeBeforeAdmission",
                "Planned discharge must not be before admission.");
        }

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
                    "Hospitalizations.ExaminationClinicMismatch",
                    "Examination clinic does not match hospitalization clinic.");
            }

            if (exam.PetId != request.PetId)
            {
                return Result<Guid>.Failure(
                    "Hospitalizations.ExaminationPetMismatch",
                    "Examination pet does not match hospitalization pet.");
            }
        }

        var activeExists = await _hospitalizationsRead.AnyAsync(
            new ActiveHospitalizationForPetClinicSpec(tenantId, effectiveClinicId, request.PetId, null),
            ct);
        if (activeExists)
        {
            return Result<Guid>.Failure(
                "Hospitalizations.ActiveHospitalizationExists",
                "An active hospitalization already exists for this pet at this clinic.");
        }

        Hospitalization entity;
        try
        {
            entity = new Hospitalization(
                tenantId,
                effectiveClinicId,
                request.PetId,
                request.ExaminationId,
                admittedUtc,
                request.PlannedDischargeAtUtc,
                request.Reason,
                request.Notes);
        }
        catch (ArgumentException ex)
        {
            return Result<Guid>.Failure("Hospitalizations.Validation", ex.Message);
        }

        await _hospitalizationsWrite.AddAsync(entity, ct);
        await _hospitalizationsWrite.SaveChangesAsync(ct);
        return Result<Guid>.Success(entity.Id);
    }
}
