namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// İstek kapsamında çözümlenmiş kiracı: yalnız JWT <c>tenant_id</c> (sorgu <c>tenantId</c> kiracı belirlemez).
/// </summary>
public interface ITenantContext
{
    /// <summary>Çözümlenmiş kiracı; yoksa <c>null</c> (handler doğrulamasına düşer).</summary>
    Guid? TenantId { get; }
}
