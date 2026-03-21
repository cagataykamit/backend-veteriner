using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Persistence.Seeding;

public static class DataSeeder
{
    public static async Task SeedAsync(
        AppDbContext db,
        IPasswordHasher hasher,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        await PermissionSeeder.SeedAsync(db, logger, ct);

        var user = await db.Users
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Email == "admin@example.com", ct);

        if (user is null)
        {
            user = new User("admin@example.com", hasher.Hash("123456"));
            user.AddRole("Admin");

            await db.Users.AddAsync(user, ct);
            await db.SaveChangesAsync(ct);

            logger?.LogInformation("Default admin user seeded.");
            return;
        }

        var isBcrypt = user.PasswordHash?.StartsWith("$2a$") == true
            || user.PasswordHash?.StartsWith("$2b$") == true
            || user.PasswordHash?.StartsWith("$2y$") == true;

        if (!isBcrypt)
        {
            db.Users.RemoveRange(db.Users.Where(u => u.Email == "admin@example.com"));
            await db.SaveChangesAsync(ct);

            var newAdmin = new User("admin@example.com", hasher.Hash("123456"));
            newAdmin.AddRole("Admin");

            await db.Users.AddAsync(newAdmin, ct);
            await db.SaveChangesAsync(ct);

            logger?.LogWarning("Default admin user recreated with bcrypt password hash.");
        }
    }
}
