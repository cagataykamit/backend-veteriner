using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Domain.Authorization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Persistence.Seeding;

public static class PermissionSeeder
{
    private const int PermissionSaveMaxAttempts = 5;

    public static async Task SeedAsync(
        AppDbContext db,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        var catalog = PermissionCatalog.All
            .Select(x => new PermissionDefinition(
                x.Code.Trim(),
                x.Description.Trim(),
                x.Group.Trim()))
            .ToList();

        var duplicateCodes = catalog
            .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateCodes.Count > 0)
        {
            throw new InvalidOperationException(
                $"PermissionCatalog içinde duplicate code bulundu: {string.Join(", ", duplicateCodes)}");
        }

        var catalogCodes = new HashSet<string>(
            catalog.Select(x => x.Code),
            StringComparer.OrdinalIgnoreCase);

        var finalAdded = 0;
        var finalUpdated = 0;

        for (var attempt = 1; attempt <= PermissionSaveMaxAttempts; attempt++)
        {
            var (addedCount, updatedCount) = await ApplyCatalogToTrackedPermissionsAsync(db, catalog, ct);
            finalAdded = addedCount;
            finalUpdated = updatedCount;

            if (!db.ChangeTracker.HasChanges())
                break;

            try
            {
                await db.SaveChangesAsync(ct);
                break;
            }
            catch (DbUpdateException ex) when (
                attempt < PermissionSaveMaxAttempts
                && IsDuplicatePermissionsCodeConstraintViolation(ex))
            {
                DetachAddedPermissionEntities(db);
            }
        }

        var dbPermissionsAfterSave = await db.Set<Permission>().ToListAsync(ct);

        var orphanedPermissions = dbPermissionsAfterSave
            .Where(x => !catalogCodes.Contains(x.Code))
            .OrderBy(x => x.Code)
            .ToList();

        logger?.LogInformation(
            "Permission seeding tamamland?. Added: {AddedCount}, Updated: {UpdatedCount}, Orphaned: {OrphanedCount}",
            finalAdded,
            finalUpdated,
            orphanedPermissions.Count);

        if (orphanedPermissions.Count > 0)
        {
            logger?.LogWarning(
                "Catalog d???nda kalan permission kay?tlar? bulundu ve siliniyor: {PermissionCodes}",
                string.Join(", ", orphanedPermissions.Select(x => x.Code)));

            db.Permissions.RemoveRange(orphanedPermissions);
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task<(int AddedCount, int UpdatedCount)> ApplyCatalogToTrackedPermissionsAsync(
        AppDbContext db,
        IReadOnlyList<PermissionDefinition> catalog,
        CancellationToken ct)
    {
        var dbPermissions = await db.Set<Permission>().ToListAsync(ct);

        var dbByCode = dbPermissions.ToDictionary(
            x => x.Code,
            x => x,
            StringComparer.OrdinalIgnoreCase);

        var addedCount = 0;
        var updatedCount = 0;

        foreach (var item in catalog)
        {
            if (!dbByCode.TryGetValue(item.Code, out var existing))
            {
                db.Add(new Permission(item.Code, item.Description, item.Group));
                addedCount++;
                continue;
            }

            var descriptionChanged = !string.Equals(
                existing.Description,
                item.Description,
                StringComparison.Ordinal);

            var groupChanged = !string.Equals(
                existing.Group,
                item.Group,
                StringComparison.Ordinal);

            if (descriptionChanged || groupChanged)
            {
                existing.UpdateDetails(item.Description, item.Group);
                updatedCount++;
            }
        }

        return (addedCount, updatedCount);
    }

    private static void DetachAddedPermissionEntities(AppDbContext db)
    {
        foreach (var entry in db.ChangeTracker.Entries<Permission>()
                     .Where(e => e.State == EntityState.Added)
                     .ToList())
            entry.State = EntityState.Detached;
    }

    /// <summary>
    /// Paralel seed / concurrent insert yarışında <c>IX_Permissions_Code</c> ihlali —
    /// yalnızca bu senaryo için true döner.
    /// </summary>
    private static bool IsDuplicatePermissionsCodeConstraintViolation(DbUpdateException ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            if (e is not SqlException sql)
                continue;

            if (sql.Number is not (2601 or 2627))
                continue;

            return sql.Message.Contains("IX_Permissions_Code", StringComparison.OrdinalIgnoreCase)
                   || (sql.Message.Contains("dbo.Permissions", StringComparison.OrdinalIgnoreCase)
                       && sql.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }
}
