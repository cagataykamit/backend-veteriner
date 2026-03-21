using Backend.Veteriner.Application.Auth.Contracts;
using Backend.Veteriner.Application.Common.Abstractions;
using Microsoft.Extensions.Caching.Memory;

namespace Backend.Veteriner.Infrastructure.Caching;

/// <summary>
/// IMemoryCache üzerinde kullanıcı permission cache’ini düşüren implementasyon.
/// </summary>
public sealed class PermissionCacheInvalidator : IPermissionCacheInvalidator
{
    private readonly IMemoryCache _cache;

    public PermissionCacheInvalidator(IMemoryCache cache) => _cache = cache;

    public void InvalidateUser(Guid userId)
        => _cache.Remove(PermissionCacheKeys.UserPermissions(userId));

    public void InvalidateUsers(IEnumerable<Guid> userIds)
    {
        foreach (var userId in userIds)
            _cache.Remove(PermissionCacheKeys.UserPermissions(userId));
    }
}
