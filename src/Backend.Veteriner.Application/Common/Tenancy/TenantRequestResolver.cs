using System.Security.Claims;
using Backend.Veteriner.Application.Common.Constants;

namespace Backend.Veteriner.Application.Common.Tenancy;

/// <summary>
/// İstek kiracısı yalnız imzalı JWT <c>tenant_id</c>'den gelir. Sorgu <c>tenantId</c> kiracı belirlemez: JWT'dekinden
/// farklıysa istek güvenlik için reddedilir; JWT'de kiracı yoksa yok sayılır (üyelik denetimi olmadan başka kiracıyı
/// hedeflemeye izin vermemek için).
/// </summary>
public static class TenantRequestResolver
{
    public static TenantResolveResult Resolve(IEnumerable<Claim> claims, string? queryTenantIdRaw)
    {
        var claimRaw = claims.FirstOrDefault(c => c.Type == VeterinerClaims.TenantId)?.Value;
        Guid? claimTenant = Guid.TryParse(claimRaw, out var c) ? c : null;
        Guid? queryTenant = Guid.TryParse(queryTenantIdRaw, out var q) ? q : null;

        if (claimTenant.HasValue && queryTenant.HasValue && claimTenant.Value != queryTenant.Value)
            return new TenantResolveResult(null, TenantConflict: true);

        return new TenantResolveResult(claimTenant, TenantConflict: false);
    }
}

public readonly record struct TenantResolveResult(Guid? TenantId, bool TenantConflict);
