using Backend.Veteriner.Application.Auth.Contracts;
using Backend.Veteriner.Application.Common.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;
using System.Security.Claims;

namespace Backend.Veteriner.Infrastructure.Caching;

/// <summary>
/// Kullan�c�n�n efektif permission kodlar�n� okur ve cache�ler.
/// - Cache stampede engeli: user bazl� SemaphoreSlim
/// - TTL: 10 dk + jitter (thundering herd azalt�r)
/// Not: Cache invalidation sorumlulu�u bu s�n�fta de�ildir (IPermissionCacheInvalidator).
/// </summary>
public sealed class PermissionReader : IPermissionReader
{
    private readonly IOperationClaimPermissionRepository _ocpRepo;
    private readonly IMemoryCache _cache;

    // Cache stampede �nlemek i�in user bazl� kilit
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Locks = new();

    // Kurumsal varsay�lan TTL: 10 dk + jitter
    private static readonly TimeSpan BaseTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan JitterMax = TimeSpan.FromMinutes(2);

    public PermissionReader(IOperationClaimPermissionRepository ocpRepo, IMemoryCache cache)
    {
        _ocpRepo = ocpRepo;
        _cache = cache;
    }

    public async Task<IReadOnlyList<string>> GetPermissionsAsync(
        Guid userId,
        ClaimsPrincipal? principal,
        CancellationToken ct = default)
    {
        // Cache key standard� (tek kaynak)
        var cacheKey = PermissionCacheKeys.UserPermissions(userId);

        // 1) H�zl� yol: cache hit
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<string>? cached) && cached is not null)
            return cached;

        // 2) Cache miss: stampede engeli i�in user bazl� kilit
        var sem = Locks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));

        await sem.WaitAsync(ct);
        try
        {
            // Double-check
            if (_cache.TryGetValue(cacheKey, out cached) && cached is not null)
                return cached;

            // 3) DB�den ger�ek permission setini oku (user -> roles -> claim -> permissions)
            var list = await _ocpRepo.GetPermissionCodesByUserIdAsync(userId, ct);

            // 4) TTL + jitter
            var ttl = BaseTtl + TimeSpan.FromSeconds(
                Random.Shared.Next(0, (int)JitterMax.TotalSeconds));

            // 5) Cache�e yaz
            _cache.Set(cacheKey, list, ttl);

            return list;
        }
        finally
        {
            sem.Release();

            // Dictionary b�y�mesini s�n�rlamak i�in basit temizlik
            if (sem.CurrentCount == 1)
                Locks.TryRemove(userId, out _);
        }
    }
}
