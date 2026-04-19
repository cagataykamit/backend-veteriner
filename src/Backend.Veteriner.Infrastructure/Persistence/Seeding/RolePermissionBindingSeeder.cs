using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Persistence.Seeding;

/// <summary>
/// <see cref="RolePermissionBindings"/> tablosundaki rol -> permission eşleştirmelerini
/// idempotent olarak <c>OperationClaimPermissions</c>'a uygular.
/// <para>
/// İdempotent garantileri:
/// <list type="bullet">
///   <item>Aynı (OperationClaimId, PermissionId) satırı zaten varsa yeni satır eklenmez.</item>
///   <item>OperationClaim veya Permission satırı yoksa skip + warning (upstream seeder'ların
///   önce çalışması beklenir: <c>PermissionSeeder</c> → <c>InviteAssignableOperationClaimsSeeder</c>).</item>
///   <item>Bu seeder var olan atamaları silmez; sadece eklenecek olanları ekler (ek üst rol).</item>
/// </list>
/// </para>
/// <para>
/// Çalıştırma sırası (bkz. <c>DbMigrator.Program.RunSeedPipelineAsync</c>):
/// <c>PermissionSeeder → DataSeeder → AdminClaimSeeder → InviteAssignableOperationClaimsSeeder → RolePermissionBindingSeeder</c>.
/// </para>
/// </summary>
public static class RolePermissionBindingSeeder
{
    public static async Task SeedAsync(AppDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        if (RolePermissionBindings.Map.Count == 0)
        {
            logger?.LogInformation("RolePermissionBindings boş; seeder no-op.");
            return;
        }

        var claims = await db.Set<OperationClaim>().ToListAsync(ct);
        var permissions = await db.Set<Permission>().ToListAsync(ct);

        var claimByName = claims.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
        var permissionByCode = permissions.ToDictionary(p => p.Code, p => p, StringComparer.OrdinalIgnoreCase);

        var totalAdded = 0;

        foreach (var (roleName, permissionCodes) in RolePermissionBindings.Map)
        {
            if (!claimByName.TryGetValue(roleName, out var claim))
            {
                logger?.LogWarning(
                    "RolePermissionBindingSeeder: '{RoleName}' OperationClaim bulunamadı, atama atlandı.",
                    roleName);
                continue;
            }

            var existingPermissionIds = await db.OperationClaimPermissions
                .Where(x => x.OperationClaimId == claim.Id)
                .Select(x => x.PermissionId)
                .ToListAsync(ct);

            var existingSet = existingPermissionIds.ToHashSet();
            var roleAdded = 0;

            foreach (var code in permissionCodes)
            {
                if (!permissionByCode.TryGetValue(code, out var permission))
                {
                    logger?.LogWarning(
                        "RolePermissionBindingSeeder: '{PermissionCode}' Permission bulunamadı ('{RoleName}' için atlandı).",
                        code,
                        roleName);
                    continue;
                }

                if (existingSet.Contains(permission.Id))
                    continue;

                await db.OperationClaimPermissions.AddAsync(
                    new OperationClaimPermission(claim.Id, permission.Id),
                    ct);
                existingSet.Add(permission.Id);
                roleAdded++;
            }

            if (roleAdded > 0)
            {
                await db.SaveChangesAsync(ct);
                logger?.LogInformation(
                    "RolePermissionBindingSeeder: '{RoleName}' rolüne {Count} yeni permission eklendi.",
                    roleName,
                    roleAdded);
            }

            totalAdded += roleAdded;
        }

        logger?.LogInformation(
            "RolePermissionBindingSeeder tamamlandı. Toplam eklenen bağ: {TotalAdded}.",
            totalAdded);
    }
}
