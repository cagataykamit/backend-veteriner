using Backend.Veteriner.Application.Administrations.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Application.Prescriptions.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Treatments.Access;
using Backend.Veteriner.Application.Treatments.Specs;
using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Treatments;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Treatments.Commands.Cancel;

/// <summary>
/// Tedaviyi gerekçeyle iptal eder (silmez). Kesinleşmiş muayene kilidi iptali engellemez (düzeltme yolu "gerekçeli iptal + yeni
/// kayıt", ADR-013). Tedaviye bağlı etkin reçete ya da uygulama varsa reddedilir: önce onlar iptal edilir (uygulama iptali stok
/// sonucu seçimi ister, tedavi iptalinin arkasına gizlenemez).
/// </summary>
public sealed class CancelTreatmentCommandHandler : IRequestHandler<CancelTreatmentCommand, Result>
{
    public const string LinkedRecordsActiveCode = "Treatments.LinkedRecordsActive";

    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IRepository<Treatment> _treatments;
    private readonly IReadRepository<Prescription> _prescriptions;
    private readonly IReadRepository<Administration> _administrations;
    private readonly TimeProvider _timeProvider;

    public CancelTreatmentCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IRepository<Treatment> treatments,
        IReadRepository<Prescription> prescriptions,
        IReadRepository<Administration> administrations,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _treatments = treatments;
        _prescriptions = prescriptions;
        _administrations = administrations;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(CancelTreatmentCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (_clientContext.UserId is not { } userId)
            return Result.Failure("Auth.UserContextMissing", "Kullanıcı bağlamı bulunamadı.");

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result.Failure("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
            return Result.Failure("Tenants.TenantInactive", "Pasif kiracı için tedavi kaydı iptal edilemez.");

        var t = await _treatments.FirstOrDefaultAsync(new TreatmentByIdSpec(tenantId, request.Id), ct);
        if (t is null || (_clinicContext.ClinicId is { } ctxClinicId && t.ClinicId != ctxClinicId))
            return Result.Failure("Treatments.NotFound", "Tedavi kaydı bulunamadı.");

        var clinicAccess = await TreatmentClinicWriteScope.EnsureWriteAccessAsync(_clinicScopeResolver, tenantId, t.ClinicId, ct);
        if (!clinicAccess.IsSuccess)
            return clinicAccess;

        if (!ExaminationRowVersion.TryDecode(request.RowVersion, out var expectedRowVersion))
            return Result.Failure("Treatments.Validation", "Kayıt sürümü eksik ya da geçersiz. Sayfayı yenileyip tekrar deneyin.");

        if (!t.IsCancelled
            && (await _prescriptions.AnyAsync(new ActivePrescriptionsByTreatmentSpec(tenantId, t.Id), ct)
                || await _administrations.AnyAsync(new ActiveAdministrationsByTreatmentSpec(tenantId, t.Id), ct)))
        {
            return Result.Failure(
                LinkedRecordsActiveCode,
                "Tedaviye bağlı iptal edilmemiş reçete ya da uygulama var; önce onları iptal edin.");
        }

        var cancelled = t.Cancel(userId, request.Reason, _timeProvider.GetUtcNow().UtcDateTime);
        if (!cancelled.IsSuccess)
            return cancelled;

        t.SetExpectedRowVersion(expectedRowVersion);

        try
        {
            await _treatments.UpdateAsync(t, ct);
            await _treatments.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(TreatmentConcurrency.ConflictCode, TreatmentConcurrency.ConflictMessage);
        }

        return Result.Success();
    }
}
