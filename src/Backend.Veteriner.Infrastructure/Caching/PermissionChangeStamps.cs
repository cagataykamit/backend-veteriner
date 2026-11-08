using Backend.Veteriner.Infrastructure.Security;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Caching;

/// <summary>
/// İzin değişim damgası (SEC-002). Kullanıcının izinleri, rolleri, kiracı üyeliği ya da klinik ataması değişince
/// <see cref="PermissionCacheInvalidator"/> o anı damgalar; damgadan önce üretilmiş erişim token'ı kimlik doğrulamada
/// reddedilir (401) ve istemci yenileme ucundan güncel izinle yeni token alır.
/// <para>
/// Damgalar süreç belleğinde tutulur: <b>tek API süreci varsayımı</b>. Süreç başlangıcı da genel damgadır; yeniden
/// başlatmada damgalar kaybolsa bile başlangıçtan önce üretilmiş token kabul edilmez. Birden çok API süreci
/// çalıştırılırsa damga paylaşılan önbelleğe ya da kullanıcı satırına taşınmalıdır (<c>docs/SECURITY_CONFIGURATION.md</c>).
/// </para>
/// </summary>
public sealed class PermissionChangeStamps
{
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _timeProvider;
    private readonly MemoryCacheEntryOptions _entryOptions;
    private readonly long _processStartedAtUnixSeconds;

    public PermissionChangeStamps(IMemoryCache cache, TimeProvider timeProvider, IOptions<JwtOptions> jwtOptions)
    {
        _cache = cache;
        _timeProvider = timeProvider;

        // Damga, ondan önce üretilmiş token'ların süresi dolana kadar tutulur; sonrası gereksizdir.
        _entryOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(jwtOptions.Value.ExpMinutes),
            Priority = CacheItemPriority.NeverRemove
        };

        _processStartedAtUnixSeconds = timeProvider.GetUtcNow().ToUnixTimeSeconds();
    }

    /// <summary>Kullanıcının izinlerinin şu an değiştiğini damgalar.</summary>
    public void Mark(Guid userId)
        => _cache.Set(PermissionCacheKeys.UserPermissionChangeStamp(userId), _timeProvider.GetUtcNow().ToUnixTimeSeconds(), _entryOptions);

    /// <summary>
    /// Token'ın üretim anı (<c>iat</c>, saniye) damgadan önceyse true. Eşitlik kabul edilir: damgayla aynı saniyede
    /// yenilenen token da reddedilseydi yenileme döngüye girerdi.
    /// </summary>
    public bool IsStale(Guid userId, long issuedAtUnixSeconds)
    {
        if (issuedAtUnixSeconds < _processStartedAtUnixSeconds)
            return true;

        return _cache.TryGetValue(PermissionCacheKeys.UserPermissionChangeStamp(userId), out long stamp)
               && issuedAtUnixSeconds < stamp;
    }
}
