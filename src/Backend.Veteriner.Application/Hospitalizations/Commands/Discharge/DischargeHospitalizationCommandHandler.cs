using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Application.Hospitalizations.Access;
using Backend.Veteriner.Application.Hospitalizations.Contracts.Dtos;
using Backend.Veteriner.Application.Hospitalizations.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Hospitalizations.Commands.Discharge;

public sealed class DischargeHospitalizationCommandHandler
    : IRequestHandler<DischargeHospitalizationCommand, Result<HospitalizationWriteResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IRepository<Hospitalization> _hospitalizationsWrite;
    private readonly TimeProvider _timeProvider;

    public DischargeHospitalizationCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IRepository<Hospitalization> hospitalizationsWrite,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _hospitalizationsWrite = hospitalizationsWrite;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<HospitalizationWriteResultDto>> Handle(DischargeHospitalizationCommand request, CancellationToken ct)
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
                "Pasif kiracı için taburcu işlemi yapılamaz.");
        }

        var row = await _hospitalizationsWrite.FirstOrDefaultAsync(
            new HospitalizationForUpdateByIdSpec(tenantId, request.Id), ct);
        if (row is null)
            return Fail("Hospitalizations.NotFound", "Yatış kaydı bulunamadı.");

        if (_clinicContext.ClinicId is { } ctxClinicId && row.ClinicId != ctxClinicId)
            return Fail("Hospitalizations.NotFound", "Yatış kaydı bulunamadı.");

        var clinicAccess = await HospitalizationClinicWriteScope.EnsureWriteAccessAsync(
            _clinicScopeResolver, tenantId, row.ClinicId, ct);
        if (!clinicAccess.IsSuccess)
            return Result<HospitalizationWriteResultDto>.Failure(clinicAccess.Error);

        if (!ExaminationRowVersion.TryDecodeOptional(request.RowVersion, out var expectedRowVersion))
            return Fail("Hospitalizations.Validation", "RowVersion geçerli Base64 (8 bayt) olmalıdır.");

        var dischargedUtc = AdmittedAtUtcWindow.ToUtc(request.DischargedAtUtc);
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var discharge = row.Discharge(dischargedUtc, request.DischargeNote, utcNow);
        if (!discharge.IsSuccess)
            return Result<HospitalizationWriteResultDto>.Failure(discharge.Error);

        if (expectedRowVersion is not null)
            row.SetExpectedRowVersion(expectedRowVersion);

        try
        {
            await _hospitalizationsWrite.UpdateAsync(row, ct);
            await _hospitalizationsWrite.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail(
                "Hospitalizations.ConcurrencyConflict",
                "Yatış kaydı eşzamanlı olarak güncellendi; formu yenileyip tekrar deneyin.");
        }

        return Result<HospitalizationWriteResultDto>.Success(
            new HospitalizationWriteResultDto(row.Id, ExaminationRowVersion.Encode(row.RowVersion)));
    }

    private static Result<HospitalizationWriteResultDto> Fail(string code, string message)
        => Result<HospitalizationWriteResultDto>.Failure(code, message);
}
