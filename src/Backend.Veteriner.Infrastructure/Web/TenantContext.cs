using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Tenancy;
using Microsoft.AspNetCore.Http;

namespace Backend.Veteriner.Infrastructure.Web;

public sealed class TenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _accessor;

    public TenantContext(IHttpContextAccessor accessor) => _accessor = accessor;

    public Guid? TenantId
    {
        get
        {
            // HTTP isteği yoksa (arka plan işi) yalnız açıkça başlatılmış kiracı kapsamı geçerlidir.
            if (_accessor.HttpContext is null)
                return BackgroundTenantScope.TenantId;

            var items = _accessor.HttpContext.Items;
            if (items is null ||
                !items.TryGetValue(TenantHttpContextKeys.ResolvedTenantId, out var raw) ||
                raw is not Guid g)
                return null;
            return g;
        }
    }
}
