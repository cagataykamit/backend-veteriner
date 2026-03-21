using Backend.Veteriner.Application.Common.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Common;

/// <summary>Controller'larda çözümlenmiş kiracıyı kullanma yardımcıları.</summary>
public static class TenantHttpExtensions
{
    /// <summary>
    /// Middleware'in birleştirdiği kiracıyı döndürür; yoksa 400 ProblemDetails.
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
            detail: "tenant_id JWT claim (login/refresh isteğinde isteğe bağlı tenantId) veya sorgu tenantId gerekir.");
        return false;
    }

    /// <summary>
    /// JWT/sorgu kiracısı varken gövde tenantId ile uyuşmazsa 403.
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
