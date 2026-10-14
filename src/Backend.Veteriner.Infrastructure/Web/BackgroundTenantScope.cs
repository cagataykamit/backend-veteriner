namespace Backend.Veteriner.Infrastructure.Web;

/// <summary>
/// HTTP isteği olmayan arka plan işinde (içe aktarma uygulaması) mevcut komutların kiracı bağlamı. Yalnız
/// <see cref="TenantContext"/> HTTP bağlamı yokken okur; HTTP isteğinde hiçbir etkisi yoktur. Değer akış yönünde
/// (AsyncLocal) taşınır ve <see cref="Begin"/> dönüşü atılınca önceki değere döner.
/// </summary>
public static class BackgroundTenantScope
{
    private static readonly AsyncLocal<Guid?> Current = new();

    public static Guid? TenantId => Current.Value;

    public static IDisposable Begin(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));

        var previous = Current.Value;
        Current.Value = tenantId;
        return new Restore(previous);
    }

    private sealed class Restore(Guid? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
