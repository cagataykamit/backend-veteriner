using Backend.Veteriner.Application.Common.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Common;

/// <summary>Controller'larda çözümlenmiş kiracıyı kullanma yardımcıları.</summary>
public static class TenantHttpExtensions
{
    /// <summary>
    /// Çözümlenmiş kiracıyı döndürür (<see cref="ITenantContext"/> = JWT <c>tenant_id</c> ve/veya sorgu <c>tenantId</c>).
    /// Tenant-scoped API'lerde handler kaynağı budur; gövdede ayrıca tenant beklenmez.
    /// </summary>
    public static bool TryGetResolvedTenant(
        this ControllerBase controller,
        ITenantContext tenantContext,
        out Guid tenantId,
        out IActionResult? problem)
    {
        if (tenantContext.TenantId is { } id)
        {
            tenantId = id;
            problem = null;
            return true;
        }

        tenantId = default;
        problem = controller.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Kiracı eksik",
            detail: "Kiracı bağlamı yok: geçerli access token içinde tenant_id claim veya (geçiş için) sorgu tenantId gerekir.");
        return false;
    }

    /// <summary>
    /// JWT/sorgu kiracısı varken gövde <c>tenantId</c> ile uyuşmazsa 403.
    /// Yeni uçlarda kullanılmıyor; geriye dönük veya özel senaryolar içindir.
    /// </summary>
    public static IActionResult? ValidateBodyTenant(
        this ControllerBase controller,
        ITenantContext tenantContext,
        Guid bodyTenantId)
    {
        if (!tenantContext.TenantId.HasValue)
            return null;

        if (tenantContext.TenantId.Value != bodyTenantId)
        {
            return controller.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Tenant uyuşmazlığı",
                detail: "Çözümlenmiş kiracı ile istek gövdesindeki tenantId eşleşmiyor.");
        }

        return null;
    }
}
