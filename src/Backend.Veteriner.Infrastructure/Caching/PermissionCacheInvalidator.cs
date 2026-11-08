using Backend.Veteriner.Application.Auth.Contracts;
using Backend.Veteriner.Application.Common.Abstractions;
using Microsoft.Extensions.Caching.Memory;

namespace Backend.Veteriner.Infrastructure.Caching;

/// <summary>
/// IMemoryCache üzerinde kullanıcı permission cache’ini düşüren implementasyon.
/// Aynı anda izin değişim damgası da yazılır (SEC-002): kullanıcının eldeki erişim token'ı reddedilir, yenilemede güncel izin gelir.
/// </summary>
public sealed class PermissionCacheInvalidator : IPermissionCacheInvalidator
{
    private readonly IMemoryCache _cache;
    private readonly PermissionChangeStamps _stamps;

    public PermissionCacheInvalidator(IMemoryCache cache, PermissionChangeStamps stamps)
    {
        _cache = cache;
        _stamps = stamps;
    }

    public void InvalidateUser(Guid userId)
    {
        _cache.Remove(PermissionCacheKeys.UserPermissions(userId));
        _stamps.Mark(userId);
    }

    public void InvalidateUsers(IEnumerable<Guid> userIds)
    {
        foreach (var userId in userIds)
            InvalidateUser(userId);
    }
}
