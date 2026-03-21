using System.Security.Claims;
using Backend.Veteriner.Application.Common.Constants;

namespace Backend.Veteriner.Application.Common.Tenancy;

/// <summary>JWT <c>tenant_id</c> ile sorgu <c>tenantId</c> birleştirmesi; çakışmada güvenli reddetme.</summary>
public static class TenantRequestResolver
{
    public static TenantResolveResult Resolve(IEnumerable<Claim> claims, string? queryTenantIdRaw)
    {
        var claimRaw = claims.FirstOrDefault(c => c.Type == VeterinerClaims.TenantId)?.Value;
        Guid? claimTenant = Guid.TryParse(claimRaw, out var c) ? c : null;
        Guid? queryTenant = Guid.TryParse(queryTenantIdRaw, out var q) ? q : null;

        if (claimTenant.HasValue && queryTenant.HasValue && claimTenant.Value != queryTenant.Value)
            return new TenantResolveResult(null, TenantConflict: true);

        var effective = claimTenant ?? queryTenant;
        return new TenantResolveResult(effective, TenantConflict: false);
    }
}

public readonly record struct TenantResolveResult(Guid? TenantId, bool TenantConflict);
