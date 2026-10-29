using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Reports.Preferences;

/// <summary>
/// Rapor tercihleri (favori, kayıtlı filtre) kiracı + kullanıcı kapsamındadır: ikisi de JWT'den gelir, istekten alınmaz.
/// </summary>
internal static class ReportPreferenceScope
{
    public static readonly Error TenantContextMissing =
        new("Tenants.ContextMissing", "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");

    public static readonly Error UserContextMissing =
        new("Auth.Unauthorized.UserContextMissing", "Kullanıcı bağlamı yok.");

    public static Result<(Guid TenantId, Guid UserId)> Resolve(ITenantContext tenantContext, IClientContext clientContext)
    {
        if (tenantContext.TenantId is not { } tenantId)
            return Result<(Guid, Guid)>.Failure(TenantContextMissing);

        if (clientContext.UserId is not { } userId)
            return Result<(Guid, Guid)>.Failure(UserContextMissing);

        return Result<(Guid, Guid)>.Success((tenantId, userId));
    }
}
