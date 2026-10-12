using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Application.Examinations.Access;
using Backend.Veteriner.Application.LabResults.Access;
using Backend.Veteriner.Application.LabResults.Contracts.Dtos;
using Backend.Veteriner.Application.LabResults.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.LabResults.Commands.Cancel;

public sealed class CancelLabResultCommandHandler
    : IRequestHandler<CancelLabResultCommand, Result<LabResultWriteResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IRepository<LabResult> _labResultsWrite;

    public CancelLabResultCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IReadRepository<Examination> examinations,
        IRepository<LabResult> labResultsWrite)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _examinations = examinations;
        _labResultsWrite = labResultsWrite;
    }

    public async Task<Result<LabResultWriteResultDto>> Handle(CancelLabResultCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Fail(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (_clientContext.UserId is not { } userId)
            return Fail("Auth.UserContextMissing", "Kullanıcı bağlamı bulunamadı.");

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Fail("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
            return Fail("Tenants.TenantInactive", "Pasif kiracı için laboratuvar kaydı iptal edilemez.");

        var existing = await _labResultsWrite.FirstOrDefaultAsync(
            new LabResultForUpdateByIdSpec(tenantId, request.Id), ct);
        if (existing is null
            || (_clinicContext.ClinicId is { } contextClinicId && existing.ClinicId != contextClinicId))
        {
            return Fail("LabResults.NotFound", "Laboratuvar sonucu bulunamadı.");
        }

        var clinicAccess = await LabResultClinicWriteScope.EnsureWriteAccessAsync(
            _clinicScopeResolver, tenantId, existing.ClinicId, ct);
        if (!clinicAccess.IsSuccess)
            return Result<LabResultWriteResultDto>.Failure(clinicAccess.Error);

        if (!ExaminationRowVersion.TryDecode(request.RowVersion, out var expectedRowVersion))
            return Fail("LabResults.Validation", "RowVersion zorunludur ve geçerli Base64 (8 bayt) olmalıdır.");

        var locked = await ExaminationLinkedRecordLock.EnsureLabCancellableAsync(_examinations, tenantId, existing, ct);
        if (!locked.IsSuccess)
            return Result<LabResultWriteResultDto>.Failure(locked.Error);

        var cancelled = existing.Cancel(userId, request.Reason);
        if (!cancelled.IsSuccess)
            return Result<LabResultWriteResultDto>.Failure(cancelled.Error);

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
