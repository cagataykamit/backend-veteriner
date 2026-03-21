using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Persistence.Seeding;

public static class PermissionSeeder
{
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
                $"PermissionCatalog i�inde duplicate code bulundu: {string.Join(", ", duplicateCodes)}");
        }

        var dbPermissions = await db.Set<Permission>()
            .ToListAsync(ct);

        var dbByCode = dbPermissions.ToDictionary(
            x => x.Code,
            x => x,
            StringComparer.OrdinalIgnoreCase);

        var catalogCodes = new HashSet<string>(
            catalog.Select(x => x.Code),
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

        var orphanedPermissions = dbPermissions
            .Where(x => !catalogCodes.Contains(x.Code))
            .OrderBy(x => x.Code)
            .ToList();

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(ct);
        }

        logger?.LogInformation(
            "Permission seeding tamamland?. Added: {AddedCount}, Updated: {UpdatedCount}, Orphaned: {OrphanedCount}",
            addedCount,
            updatedCount,
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
}