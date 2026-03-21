namespace Backend.Veteriner.Infrastructure.Caching;

/// <summary>
/// Permission cache key standardı.
/// Kurumsal standart: user:perms:{userId}
/// </summary>
internal static class PermissionCacheKeys
{
    public static string UserPermissions(Guid userId) => $"user:perms:{userId}";
}
