using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Common.Finance;

/// <summary>Ücret/ödeme düzeltme ve iptal komutlarının ortak ön koşulu: kiracı, kullanıcı ve aktif kiracı.</summary>
internal static class FinanceCorrectionActor
{
    public static async Task<Result<(Guid TenantId, Guid UserId)>> ResolveAsync(
        ITenantContext tenantContext,
        IClientContext clientContext,
        IReadRepository<Tenant> tenants,
        CancellationToken ct)
    {
        if (tenantContext.TenantId is not { } tenantId)
        {
            return Result<(Guid, Guid)>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (clientContext.UserId is not { } userId)
            return Result<(Guid, Guid)>.Failure("Auth.UserContextMissing", "Kullanıcı bağlamı bulunamadı.");

        var tenant = await tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result<(Guid, Guid)>.Failure("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
            return Result<(Guid, Guid)>.Failure("Tenants.TenantInactive", "Pasif kiracı için ücret/ödeme düzeltmesi yapılamaz.");

        return Result<(Guid, Guid)>.Success((tenantId, userId));
    }
}
