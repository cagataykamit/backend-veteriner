namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// İstek kapsamında çözümlenmiş kiracı. Öncelik: JWT <c>tenant_id</c>, yoksa sorgu <c>tenantId</c> (geçiş).
/// </summary>
public interface ITenantContext
{
    /// <summary>Çözümlenmiş kiracı; yoksa <c>null</c> (handler doğrulamasına düşer).</summary>
    Guid? TenantId { get; }
}
