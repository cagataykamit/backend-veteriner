using System.Collections.Generic;
using System.Reflection;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Authorization;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;

namespace Backend.IntegrationTests.Infrastructure;

internal static class TestDataSeeder
{
    /// <summary>
    /// Aynı LocalDB'ye paralel <see cref="WebApplicationFactory{TEntryPoint}"/> örnekleri bağlandığında
    /// çift insert / PK çakışmasını önlemek için seed tek iş parçacığında çalıştırılır.
    /// </summary>
    private static readonly object SeedSync = new();

    public static void Seed(AppDbContext db, IPasswordHasher hasher)
    {
        lock (SeedSync)
        {
            SeedAdminUser(db, hasher);
            EnsureDefaultTenantAndUserTenant(db);
            SeedOutboxPermissionsForAdmin(db);
        }
    }

    private static void SetEntityId(object entity, Guid id)
    {
        var prop = entity.GetType().GetProperty(
            "Id",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        prop?.SetValue(entity, id);
    }

    private static void SeedAdminUser(AppDbContext db, IPasswordHasher hasher)
    {
        const string email = "admin@example.com";
        if (db.Users.Any(u => u.Email == email))
        {
            return;
        }

        var hash = hasher.Hash("123456");
        db.Users.Add(new User(email, hash));
        db.SaveChanges();
    }

    /// <summary>
    /// Login üyelik gerektirir; entegrasyon DB'sinde varsayılan kiracı ve <see cref="UserTenant"/> yoksa giriş 400 döner.
    /// </summary>
    private static void EnsureDefaultTenantAndUserTenant(AppDbContext db)
    {
        var admin = db.Users.FirstOrDefault(u => u.Email == "admin@example.com");
        if (admin is null)
            return;

        var tenant = db.Tenants.FirstOrDefault(t => t.Name == DataSeeder.DefaultTenantName);
        if (tenant is null)
        {
            db.Tenants.Add(new Tenant(DataSeeder.DefaultTenantName));
            db.SaveChanges();
            tenant = db.Tenants.First(t => t.Name == DataSeeder.DefaultTenantName);
        }

        if (!db.UserTenants.Any(ut => ut.UserId == admin.Id && ut.TenantId == tenant.Id))
        {
            db.UserTenants.Add(new UserTenant(admin.Id, tenant.Id));
            db.SaveChanges();
        }

        var seedClinic = db.Clinics.FirstOrDefault(c =>
            c.TenantId == tenant.Id && c.Name == DataSeeder.DefaultSeedClinicName);
        if (seedClinic is null)
        {
            seedClinic = new Clinic(tenant.Id, DataSeeder.DefaultSeedClinicName, "İstanbul");
            db.Clinics.Add(seedClinic);
            db.SaveChanges();
        }

        if (!db.UserClinics.Any(uc => uc.UserId == admin.Id && uc.ClinicId == seedClinic.Id))
        {
            db.UserClinics.Add(new UserClinic(admin.Id, seedClinic.Id));
            db.SaveChanges();
        }
    }

    // Tasks modülü foundation'dan çıkarıldığı için task permission seeding kaldırıldı.

    /// <summary>
    /// Outbox API izinleri (idempotent).
    /// </summary>
    private static void SeedOutboxPermissionsForAdmin(AppDbContext db)
    {
        var admin = db.Users.First(u => u.Email == "admin@example.com");

        const string claimName = "IntegrationTasksAdmin";
        var claim = db.OperationClaims.FirstOrDefault(c => c.Name == claimName);
        if (claim is null)
        {
            claim = new OperationClaim(claimName);
            db.OperationClaims.Add(claim);
            db.SaveChanges();
        }

        var permissionCodes = new[]
        {
            PermissionCatalog.Outbox.Read,
            PermissionCatalog.Outbox.Write
        };

        foreach (var code in permissionCodes)
        {
            var perm = db.Permissions.FirstOrDefault(p => p.Code == code);
            if (perm is null)
            {
                perm = new Permission(code, code, "Outbox");
                db.Permissions.Add(perm);
                db.SaveChanges();
            }

            var linked = db.OperationClaimPermissions.Any(
                x => x.OperationClaimId == claim.Id && x.PermissionId == perm.Id);
            if (!linked)
            {
                db.OperationClaimPermissions.Add(new OperationClaimPermission(claim.Id, perm.Id));
            }
        }

        var hasUserClaim = db.UserOperationClaims.Any(
            uoc => uoc.UserId == admin.Id && uoc.OperationClaimId == claim.Id);
        if (!hasUserClaim)
        {
            db.UserOperationClaims.Add(new UserOperationClaim(admin.Id, claim.Id));
            db.SaveChanges();
        }

        db.SaveChanges();
    }

    // Organization entity fixture seeding bu adımda kaldırıldı (Tasks integration testleri runtime'da kırılabilir).
}
