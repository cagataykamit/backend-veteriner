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
///   <item>Bu seeder var olan atamaları silmez; sadece eklenecek olanları ekler (ek üst rol).
///   <b>İstisna (Faz 4B-6):</b> <c>"Admin"</c> claim'i için whitelist temizliği uygulanır — bkz. aşağıda.</item>
/// </list>
/// </para>
/// <para>
/// <b>Admin claim whitelist temizliği (Faz 4B-6):</b> Tenant Admin rolü ile platform yöneticisinin
/// ayrılması için, <c>"Admin"</c> claim'inde <see cref="PermissionCatalog"/> içinde olan ama
/// <see cref="RolePermissionBindings"/>.Map[<c>"Admin"</c>] içinde olmayan tüm permission bağları silinir.
/// Bu temizlik <b>yalnız Admin claim'i için</b> yapılır; ClinicAdmin / Veteriner / Sekreter / Owner
/// claim'lerinde mevcut bağlar (manuel atamalar dahil) korunur. Catalog dışı kalan kodlar (varsa)
/// yine korunur — onların temizliği <see cref="PermissionSeeder"/> orphan akışına aittir.
/// </para>
/// <para>
/// Çalıştırma sırası (bkz. <c>DbMigrator.Program.RunSeedPipelineAsync</c>):
/// <c>PermissionSeeder → DataSeeder → AdminClaimSeeder → InviteAssignableOperationClaimsSeeder → RolePermissionBindingSeeder</c>.
/// </para>
/// </summary>
public static class RolePermissionBindingSeeder
{
    private const string TenantAdminClaimName = "Admin";

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

        // Admin whitelist cleanup: tenant Admin rolünden sistem/whitelist-dışı permission'ları kaldır.
        var removedFromAdmin = await CleanupTenantAdminWhitelistAsync(
            db, claimByName, permissions, logger, ct);

        logger?.LogInformation(
            "RolePermissionBindingSeeder tamamlandı. Toplam eklenen bağ: {TotalAdded}, Admin claim'inden temizlenen: {RemovedFromAdmin}.",
            totalAdded,
            removedFromAdmin);
    }

    /// <summary>
    /// <c>"Admin"</c> claim'inde <see cref="PermissionCatalog"/> içinde olan ama
    /// <see cref="RolePermissionBindings"/>.Map[<c>"Admin"</c>] içinde olmayan permission bağlarını siler.
    /// <see cref="AdminClaimSeeder"/> eski sürümünden veya manuel elevation API'sinden kalan platform/sistem
    /// permission'ları (Outbox.*, Roles.*, Users.*, Permissions.*, Admin.Diagnostics, vb.) bu yolla temizlenir.
    /// Catalog dışı (orphan) kayıtlar korunur; onları <see cref="PermissionSeeder"/> ele alır.
    /// </summary>
    private static async Task<int> CleanupTenantAdminWhitelistAsync(
        AppDbContext db,
        IReadOnlyDictionary<string, OperationClaim> claimByName,
        IReadOnlyList<Permission> permissions,
        ILogger? logger,
        CancellationToken ct)
    {
        if (!claimByName.TryGetValue(TenantAdminClaimName, out var adminClaim))
        {
            logger?.LogInformation(
                "RolePermissionBindingSeeder: '{RoleName}' OperationClaim bulunamadı; whitelist temizliği atlandı.",
                TenantAdminClaimName);
            return 0;
        }

        if (!RolePermissionBindings.Map.TryGetValue(TenantAdminClaimName, out var adminCodes))
            return 0;

        var whitelist = adminCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var catalogCodes = PermissionCatalog.AllCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var permissionById = permissions.ToDictionary(p => p.Id, p => p);

        var existingBindings = await db.OperationClaimPermissions
            .Where(x => x.OperationClaimId == adminClaim.Id)
            .ToListAsync(ct);

        var toRemove = new List<OperationClaimPermission>();
        var removedCodes = new List<string>();

        foreach (var binding in existingBindings)
        {
            if (!permissionById.TryGetValue(binding.PermissionId, out var permission))
                continue; // permission satırı silinmiş; PermissionSeeder/EF cascade ele alır.

            // Catalog dışı kayıtları koru (orphan); whitelist içindeki kodları koru.
            if (!catalogCodes.Contains(permission.Code))
                continue;

            if (whitelist.Contains(permission.Code))
                continue;

            toRemove.Add(binding);
            removedCodes.Add(permission.Code);
        }

        if (toRemove.Count == 0)
            return 0;

        db.OperationClaimPermissions.RemoveRange(toRemove);
        await db.SaveChangesAsync(ct);

        logger?.LogInformation(
            "RolePermissionBindingSeeder: '{RoleName}' claim'inden {Count} whitelist-dışı permission temizlendi: {Codes}",
            TenantAdminClaimName,
            toRemove.Count,
            string.Join(", ", removedCodes.OrderBy(c => c, StringComparer.OrdinalIgnoreCase)));

        return toRemove.Count;
    }
}
