using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Seeding;

public static class AdminClaimSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        // 1?? Admin claim yoksa olu�tur
        var adminClaim = await db.Set<OperationClaim>()
            .FirstOrDefaultAsync(x => x.Name == "Admin", ct);

        if (adminClaim is null)
        {
            adminClaim = new OperationClaim("Admin");
            await db.AddAsync(adminClaim, ct);
            await db.SaveChangesAsync(ct);
        }

        // 2?? Admin kullan�c�s�n� bul
        var adminUser = await db.Users
            .FirstOrDefaultAsync(x => x.Email == "admin@example.com", ct);

        if (adminUser is null)
            return; // Orchestrator: DataSeeder �nce �al??mal?. Tek ba??na �a?r?l?rsa kullan?c? yoksa ba? kurulmaz (idempotent).

        // 3?? UserOperationClaim yoksa ekle
        var existingLink = await db.UserOperationClaims
            .AnyAsync(x => x.UserId == adminUser.Id && x.OperationClaimId == adminClaim.Id, ct);

        if (!existingLink)
        {
            var uoc = new UserOperationClaim(adminUser.Id, adminClaim.Id);
            await db.UserOperationClaims.AddAsync(uoc, ct);
            await db.SaveChangesAsync(ct);
        }

        // 4?? T�m permission'lar� getir
        var permissions = await db.Permissions.ToListAsync(ct);

        // 5?? Admin claim�e olmayan permission�lar� ba�la
        var existingPermIds = await db.OperationClaimPermissions
            .Where(x => x.OperationClaimId == adminClaim.Id)
            .Select(x => x.PermissionId)
            .ToListAsync(ct);

        var missingPerms = permissions
            .Where(p => !existingPermIds.Contains(p.Id))
            .Select(p => new OperationClaimPermission(adminClaim.Id, p.Id))
            .ToList();

        if (missingPerms.Count > 0)
        {
            await db.OperationClaimPermissions.AddRangeAsync(missingPerms, ct);
            await db.SaveChangesAsync(ct);
        }
    }
}
