using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Seeding;

/// <summary>
/// Davet whitelist'indeki <see cref="OperationClaim"/> satırlarını idempotent oluşturur (permission bağlamaz).
/// </summary>
public static class InviteAssignableOperationClaimsSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        foreach (var name in InviteAssignableOperationClaimsCatalog.NamesInDisplayOrder)
        {
            var exists = await db.OperationClaims.AnyAsync(c => c.Name == name, ct);
            if (exists)
                continue;

            await db.OperationClaims.AddAsync(new OperationClaim(name), ct);
            await db.SaveChangesAsync(ct);
        }
    }
}
