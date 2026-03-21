using System.Security.Claims;

namespace Backend.Veteriner.Application.Auth.Contracts;

/// <summary>
/// Kullanıcının efektif permission kodlarını okur.
/// Not: Cache invalidation bu interface’in sorumluluğu değildir.
/// Cache düşürme işlemleri IPermissionCacheInvalidator ile yapılır.
/// </summary>
public interface IPermissionReader
{
    Task<IReadOnlyList<string>> GetPermissionsAsync(
        Guid userId,
        ClaimsPrincipal? principal,
        CancellationToken ct = default);
}
