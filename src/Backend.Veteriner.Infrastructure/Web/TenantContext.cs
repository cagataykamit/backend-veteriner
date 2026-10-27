using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Tenancy;
using Microsoft.AspNetCore.Http;

namespace Backend.Veteriner.Infrastructure.Web;

/// <summary>
/// DI kapsamının etkin kiracısı ve kiracı filtresi kapsamı (ADR-040); kural tek yerde. Öncelik: açık
/// <see cref="UseTenant"/> kapsamı, yoksa HTTP isteğinde çözümlenmiş kiracı. HTTP isteği yoksa (arka plan işi) yalnız
/// açık kapsam geçerlidir. <c>AppDbContext</c> kiracı filtresini ve yazma korumasını buradan okur.
/// </summary>
public sealed class TenantContext : ITenantContext, ITenantScope
{
    private readonly IHttpContextAccessor _accessor;
    private Guid? _tenantOverride;

    public TenantContext(IHttpContextAccessor accessor) => _accessor = accessor;

    public Guid? TenantId => _tenantOverride ?? RequestTenantId();

    /// <summary>Kiracı filtresi kapalı mı (<see cref="AsSystem"/>).</summary>
    public bool IsSystem { get; private set; }

    public IDisposable UseTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));

        var restore = new Restore(this, _tenantOverride, IsSystem);
        _tenantOverride = tenantId;
        IsSystem = false;
        return restore;
    }

    public IDisposable AsSystem()
    {
        var restore = new Restore(this, _tenantOverride, IsSystem);
        IsSystem = true;
        return restore;
    }

    private Guid? RequestTenantId()
    {
        var items = _accessor.HttpContext?.Items;
        if (items is null ||
            !items.TryGetValue(TenantHttpContextKeys.ResolvedTenantId, out var raw) ||
            raw is not Guid g)
            return null;
        return g;
    }

    private sealed class Restore(TenantContext owner, Guid? tenantOverride, bool isSystem) : IDisposable
    {
        public void Dispose()
        {
            owner._tenantOverride = tenantOverride;
            owner.IsSystem = isSystem;
        }
    }
}
