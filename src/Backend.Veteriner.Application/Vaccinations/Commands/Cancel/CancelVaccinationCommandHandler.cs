using Backend.Veteriner.Application.Administrations.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Vaccinations.Access;
using Backend.Veteriner.Application.Vaccinations.Specs;
using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Vaccinations;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Vaccinations.Commands.Cancel;

/// <summary>
/// Aşı kaydını gerekçeyle iptal eder (silmez). Uygulamaya (stok düşümü) bağlı aşı burada iptal edilmez: iptal, stok sonucu
/// seçilerek uygulama iptaliyle yapılır (ADR-013). Kesinleşmiş muayene kilidi iptali engellemez (düzeltme yolu "gerekçeli iptal + yeni kayıt").
/// </summary>
public sealed class CancelVaccinationCommandHandler : IRequestHandler<CancelVaccinationCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IRepository<Vaccination> _vaccinations;
    private readonly IReadRepository<Administration> _administrations;
    private readonly TimeProvider _timeProvider;

    public CancelVaccinationCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IRepository<Vaccination> vaccinations,
        IReadRepository<Administration> administrations,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _vaccinations = vaccinations;
        _administrations = administrations;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(CancelVaccinationCommand request, CancellationToken ct)
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
            return Result.Failure("Tenants.TenantInactive", "Pasif kiracı için aşı kaydı iptal edilemez.");

        var v = await _vaccinations.FirstOrDefaultAsync(new VaccinationByIdSpec(tenantId, request.Id), ct);
        if (v is null || (_clinicContext.ClinicId is { } ctxClinicId && v.ClinicId != ctxClinicId))
            return Result.Failure("Vaccinations.NotFound", "Aşı kaydı bulunamadı.");

        var clinicAccess = await VaccinationClinicWriteScope.EnsureWriteAccessAsync(_clinicScopeResolver, tenantId, v.ClinicId, ct);
        if (!clinicAccess.IsSuccess)
            return clinicAccess;

        if (!ExaminationRowVersion.TryDecode(request.RowVersion, out var expectedRowVersion))
            return Result.Failure("Vaccinations.Validation", "RowVersion zorunludur ve geçerli Base64 (8 bayt) olmalıdır.");

        if (!v.IsCancelled && await _administrations.AnyAsync(new AdministrationByVaccinationIdSpec(tenantId, v.Id), ct))
        {
            return Result.Failure(
                "Vaccinations.LinkedToAdministration",
                "Uygulamaya (stok düşümü) bağlı aşı kaydı, uygulama iptaliyle iptal edilir; stok sonucu orada seçilir.");
        }

        var cancelled = v.Cancel(userId, request.Reason, _timeProvider.GetUtcNow().UtcDateTime);
        if (!cancelled.IsSuccess)
            return cancelled;

        v.SetExpectedRowVersion(expectedRowVersion);

        try
        {
            await _vaccinations.UpdateAsync(v, ct);
            await _vaccinations.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(VaccinationConcurrency.ConflictCode, VaccinationConcurrency.ConflictMessage);
        }

        return Result.Success();
    }
}
