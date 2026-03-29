using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Persistence.Seeding;

public static class DataSeeder
{
    /// <summary>Seed ile oluşturulan varsayılan kiracı adı (tekrar çalıştırmada aynı kayıt bulunur).</summary>
    public const string DefaultTenantName = "Varsayılan Kiracı";

    /// <summary>Varsayılan admin için tek bir seed klinik (tüm aktif kliniklere otomatik atama yapılmaz).</summary>
    public const string DefaultSeedClinicName = "Varsayılan Klinik";

    public static async Task SeedAsync(
        AppDbContext db,
        IPasswordHasher hasher,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        await PermissionSeeder.SeedAsync(db, logger, ct);

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Name == DefaultTenantName, ct);
        if (tenant is null)
        {
            tenant = new Tenant(DefaultTenantName);
            await db.Tenants.AddAsync(tenant, ct);
            await db.SaveChangesAsync(ct);
            logger?.LogInformation("Default tenant seeded: {Name}", DefaultTenantName);
        }

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
        }
        else
        {
            var isBcrypt = user.PasswordHash?.StartsWith("$2a$") == true
                || user.PasswordHash?.StartsWith("$2b$") == true
                || user.PasswordHash?.StartsWith("$2y$") == true;

            if (!isBcrypt)
            {
                db.Users.RemoveRange(db.Users.Where(u => u.Email == "admin@example.com"));
                await db.SaveChangesAsync(ct);

                user = new User("admin@example.com", hasher.Hash("123456"));
                user.AddRole("Admin");

                await db.Users.AddAsync(user, ct);
                await db.SaveChangesAsync(ct);

                logger?.LogWarning("Default admin user recreated with bcrypt password hash.");
            }
        }

        if (!await db.UserTenants.AnyAsync(x => x.UserId == user.Id && x.TenantId == tenant.Id, ct))
        {
            await db.UserTenants.AddAsync(new UserTenant(user.Id, tenant.Id), ct);
            await db.SaveChangesAsync(ct);
            logger?.LogInformation("UserTenant link seeded for admin and default tenant.");
        }

        var seedClinic = await db.Clinics.FirstOrDefaultAsync(
            c => c.TenantId == tenant.Id && c.Name == DefaultSeedClinicName,
            ct);
        if (seedClinic is null)
        {
            seedClinic = new Clinic(tenant.Id, DefaultSeedClinicName, "İstanbul");
            await db.Clinics.AddAsync(seedClinic, ct);
            await db.SaveChangesAsync(ct);
            logger?.LogInformation("Default seed clinic created for tenant {TenantId}.", tenant.Id);
        }

        if (!await db.UserClinics.AnyAsync(uc => uc.UserId == user.Id && uc.ClinicId == seedClinic.Id, ct))
        {
            await db.UserClinics.AddAsync(new UserClinic(user.Id, seedClinic.Id), ct);
            await db.SaveChangesAsync(ct);
            logger?.LogInformation("UserClinic seeded for admin and {Clinic}.", DefaultSeedClinicName);
        }
    }
}
