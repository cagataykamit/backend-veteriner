using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Application.Examinations.Access;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.LabResults.Access;
using Backend.Veteriner.Application.LabResults.Contracts.Dtos;
using Backend.Veteriner.Application.LabResults.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Visits.Access;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Visits;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.LabResults.Commands.Update;

public sealed class UpdateLabResultCommandHandler
    : IRequestHandler<UpdateLabResultCommand, Result<LabResultWriteResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IReadRepository<Visit> _visits;
    private readonly IRepository<LabResult> _labResultsWrite;

    public UpdateLabResultCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IReadRepository<Clinic> clinics,
        IReadRepository<Pet> pets,
        IReadRepository<Examination> examinations,
        IReadRepository<Visit> visits,
        IRepository<LabResult> labResultsWrite)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _clinics = clinics;
        _pets = pets;
        _examinations = examinations;
        _visits = visits;
        _labResultsWrite = labResultsWrite;
    }

    public async ValueTask<Result<LabResultWriteResultDto>> Handle(UpdateLabResultCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Fail(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Fail("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
        {
            return Fail(
                "Tenants.TenantInactive",
                "Pasif kiracı için laboratuvar sonucu güncellenemez.");
        }

        var existing = await _labResultsWrite.FirstOrDefaultAsync(
            new LabResultForUpdateByIdSpec(tenantId, request.Id), ct);
        if (existing is null)
            return Fail("LabResults.NotFound", "Laboratuvar sonucu bulunamadı.");

        if (_clinicContext.ClinicId.HasValue && request.ClinicId != _clinicContext.ClinicId.Value)
        {
            return Fail(
                "LabResults.ClinicContextMismatch",
                "İstek clinicId değeri aktif clinic bağlamı ile uyuşmuyor.");
        }

        var effectiveClinicId = _clinicContext.ClinicId ?? request.ClinicId;
        if (effectiveClinicId == Guid.Empty)
            effectiveClinicId = existing.ClinicId;

        if (effectiveClinicId == Guid.Empty)
            return Fail("LabResults.Validation", "ClinicId is required.");

        var clinicAccess = await LabResultClinicWriteScope.EnsureEntityAndTargetWriteAccessAsync(
            _clinicScopeResolver, tenantId, existing.ClinicId, effectiveClinicId, ct);
        if (!clinicAccess.IsSuccess)
            return Result<LabResultWriteResultDto>.Failure(clinicAccess.Error);

        if (_clinicContext.ClinicId is { } currentClinicId && effectiveClinicId != currentClinicId)
        {
            return Fail(
                "LabResults.ClinicContextMismatch",
                "Laboratuvar sonucu sadece aktif clinic bağlamında güncellenebilir.");
        }

        if (!ExaminationRowVersion.TryDecode(request.RowVersion, out var expectedRowVersion))
            return Fail("LabResults.Validation", "RowVersion zorunludur ve geçerli Base64 (8 bayt) olmalıdır.");

        DateTime? resultUtc = null;
        if (request.ResultDateUtc is { } resultDate)
        {
            resultUtc = ResultDateUtcWindow.ToUtc(resultDate);
            var window = ResultDateUtcWindow.Validate(resultUtc.Value);
            if (!window.IsSuccess)
                return Result<LabResultWriteResultDto>.Failure(window.Error);
        }

        var clinic = await _clinics.FirstOrDefaultAsync(
            new ClinicByIdSpec(tenantId, effectiveClinicId), ct);
        if (clinic is null)
            return Fail("Clinics.NotFound", "Klinik bulunamadı veya kiracıya ait değil.");

        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, request.PetId), ct);
        if (pet is null)
            return Fail("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

        Examination? exam = null;
        if (request.ExaminationId is { } examId)
        {
            exam = await _examinations.FirstOrDefaultAsync(new ExaminationByIdSpec(tenantId, examId), ct);
            if (exam is null)
            {
                return Fail(
                    "Examinations.NotFound",
                    "Muayene bulunamadı veya kiracıya ait değil.");
            }

            if (exam.ClinicId != effectiveClinicId)
            {
                return Fail(
                    "LabResults.ExaminationClinicMismatch",
                    "Examination clinic does not match lab result clinic.");
            }

            if (exam.PetId != request.PetId)
            {
                return Fail(
                    "LabResults.ExaminationPetMismatch",
                    "Examination pet does not match lab result pet.");
            }
        }

        // İptal edilmiş kayıt hiçbir alanı değişmez; kilit hatası yerine durum hatası dönmeli.
        if (existing.Status == LabResultStatus.Cancelled)
            return Fail("LabResults.AlreadyCancelled", "Laboratuvar kaydı iptal edilmiş; değiştirilemez.");

        var locked = await ExaminationLinkedRecordLock.EnsureLabIdentityEditableAsync(
            _examinations, tenantId, existing, effectiveClinicId, request.PetId, request.ExaminationId, request.TestName, ct);
        if (!locked.IsSuccess)
            return Result<LabResultWriteResultDto>.Failure(locked.Error);

        var visitHint = request.VisitId
            ?? (request.ExaminationId == existing.ExaminationId ? existing.VisitId : null);
        var visit = await LinkedRecordVisitResolver.ResolveAsync(
            _visits, tenantId, effectiveClinicId, request.PetId, exam, visitHint, "LabResults", "lab kaydı", ct);
        if (!visit.IsSuccess)
            return Result<LabResultWriteResultDto>.Failure(visit.Error);

        var update = existing.UpdateDetails(
            effectiveClinicId,
            request.PetId,
            request.ExaminationId,
            visit.Value,
            resultUtc,
            request.TestName,
            request.ResultText,
            request.Interpretation,
            request.Notes);

        if (!update.IsSuccess)
            return Result<LabResultWriteResultDto>.Failure(update.Error);

        existing.SetExpectedRowVersion(expectedRowVersion);

        try
        {
            await _labResultsWrite.UpdateAsync(existing, ct);
            await _labResultsWrite.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail(
                "LabResults.ConcurrencyConflict",
                "Laboratuvar kaydı eşzamanlı olarak güncellendi; formu yenileyip tekrar deneyin.");
        }

        return Result<LabResultWriteResultDto>.Success(
            new LabResultWriteResultDto(existing.Id, ExaminationRowVersion.Encode(existing.RowVersion)));
    }

    private static Result<LabResultWriteResultDto> Fail(string code, string message)
        => Result<LabResultWriteResultDto>.Failure(code, message);
}
