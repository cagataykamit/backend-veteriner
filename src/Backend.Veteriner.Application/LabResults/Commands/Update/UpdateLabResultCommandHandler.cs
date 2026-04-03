using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.LabResults.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.LabResults.Commands.Update;

public sealed class UpdateLabResultCommandHandler : IRequestHandler<UpdateLabResultCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IReadRepository<LabResult> _labResultsRead;
    private readonly IRepository<LabResult> _labResultsWrite;

    public UpdateLabResultCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Tenant> tenants,
        IReadRepository<Clinic> clinics,
        IReadRepository<Pet> pets,
        IReadRepository<Examination> examinations,
        IReadRepository<LabResult> labResultsRead,
        IRepository<LabResult> labResultsWrite)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _tenants = tenants;
        _clinics = clinics;
        _pets = pets;
        _examinations = examinations;
        _labResultsRead = labResultsRead;
        _labResultsWrite = labResultsWrite;
    }

    public async Task<Result> Handle(UpdateLabResultCommand request, CancellationToken ct)
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
                "Pasif kiracı için laboratuvar sonucu güncellenemez.");
        }

        var existing = await _labResultsRead.FirstOrDefaultAsync(
            new LabResultByIdSpec(tenantId, request.Id), ct);
        if (existing is null)
            return Result.Failure("LabResults.NotFound", "Laboratuvar sonucu bulunamadı.");

        if (_clinicContext.ClinicId.HasValue && request.ClinicId != _clinicContext.ClinicId.Value)
        {
            return Result.Failure(
                "LabResults.ClinicContextMismatch",
                "Istek clinicId degeri aktif clinic baglami ile uyusmuyor.");
        }

        var effectiveClinicId = _clinicContext.ClinicId ?? request.ClinicId;
        if (effectiveClinicId == Guid.Empty)
            return Result.Failure("LabResults.Validation", "ClinicId is required.");

        var resultUtc = ResultDateUtcWindow.ToUtc(request.ResultDateUtc);
        var window = ResultDateUtcWindow.Validate(resultUtc);
        if (!window.IsSuccess)
            return window;

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
                    "LabResults.ExaminationClinicMismatch",
                    "Examination clinic does not match lab result clinic.");
            }

            if (exam.PetId != request.PetId)
            {
                return Result.Failure(
                    "LabResults.ExaminationPetMismatch",
                    "Examination pet does not match lab result pet.");
            }
        }

        var update = existing.UpdateDetails(
            effectiveClinicId,
            request.PetId,
            request.ExaminationId,
            resultUtc,
            request.TestName,
            request.ResultText,
            request.Interpretation,
            request.Notes);

        if (!update.IsSuccess)
            return update;

        await _labResultsWrite.UpdateAsync(existing, ct);
        await _labResultsWrite.SaveChangesAsync(ct);
        return Result.Success();
    }
}
