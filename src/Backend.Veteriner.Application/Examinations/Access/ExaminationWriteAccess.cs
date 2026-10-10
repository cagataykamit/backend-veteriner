using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Examinations.Access;

/// <summary>
/// Kesinleştirme ve ek not handler'larının ortak ön koşulları: kiracı bağlamı ve aktifliği, muayenenin bulunması,
/// klinik ataması ve aktif klinik bağlamı. Muayene izlenmeden (AsNoTracking) yüklenir.
/// </summary>
internal static class ExaminationWriteAccess
{
    public static async Task<Result<Examination>> LoadAsync(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IReadRepository<Examination> examinations,
        Guid examinationId,
        CancellationToken ct)
    {
        if (tenantContext.TenantId is not { } tenantId)
        {
            return Result<Examination>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var tenant = await tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result<Examination>.Failure("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
        {
            return Result<Examination>.Failure(
                "Tenants.TenantInactive",
                "Pasif kiracı için muayene kaydı değiştirilemez.");
        }

        var e = await examinations.FirstOrDefaultAsync(new ExaminationForUpdateByIdSpec(tenantId, examinationId), ct);
        if (e is null)
            return Result<Examination>.Failure("Examinations.NotFound", "Muayene kaydı bulunamadı.");

        var clinicAccess = await ExaminationClinicWriteScope.EnsureWriteAccessAsync(clinicScopeResolver, tenantId, e.ClinicId, ct);
        if (!clinicAccess.IsSuccess)
            return Result<Examination>.Failure(clinicAccess.Error);

        if (clinicContext.ClinicId is { } currentClinicId && e.ClinicId != currentClinicId)
        {
            return Result<Examination>.Failure(
                "Examinations.ClinicContextMismatch",
                "Muayene kaydı sadece aktif clinic bağlamında değiştirilebilir.");
        }

        return Result<Examination>.Success(e);
    }
}
