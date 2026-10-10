using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Persistence.Seeding;

/// <summary>
/// YALNIZCA geliştirme ortamı: "Benim hastalarım" gibi hekim akışlarını denemek için tek bir test hekimi.
/// Kullanıcı, mevcut bir kiracıdaki mevcut bir kliniğe atanır (kiracı/klinik burada oluşturulmaz); e-posta doğrulanmış,
/// davet e-postası gerektirmez. Idempotent: tekrar çalıştırma kopya üretmez. Parola gerçek bir parola değildir,
/// yalnızca bu dosyada tanımlı yerel test değeridir. Üretim/staging'de çalışmaz (ortam kontrolü) ve
/// normal <c>seed</c> hattına dahil değildir; ayrı <c>seed-dev-veterinarian</c> komutuyla çalışır.
/// </summary>
public static class DevTestVeterinarianSeeder
{
    public const string TenantName = "YağmurVet";
    public const string ClinicName = "Merkez Pati";
    public const string Email = "dr.test@example.com";
    public const string DisplayName = "Dr. Test Hekim";

    /// <summary>Yerel test değeri; hiçbir gerçek hesapta kullanılmaz.</summary>
    public const string Password = "Test-Hekim-4f9Kq2";

    private const string VeterinarianClaimName = "Veteriner";

    /// <returns>Kullanıcı hazırsa true; kiracı/klinik/claim bulunamadığı için atlandıysa false.</returns>
    public static async Task<bool> SeedAsync(
        AppDbContext db,
        IPasswordHasher hasher,
        IHostEnvironment environment,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Test hekim seed'i yalnızca Development ortamında çalışır.");
        }

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Name == TenantName, ct);
        var clinic = tenant is null
            ? null
            : await db.Clinics.FirstOrDefaultAsync(
                c => c.TenantId == tenant.Id && c.Name == ClinicName && c.IsActive, ct);
        var claim = await db.OperationClaims.FirstOrDefaultAsync(c => c.Name == VeterinarianClaimName, ct);
        if (tenant is null || clinic is null || claim is null)
        {
            logger?.LogWarning(
                "Test hekim seed atlandı: kiracı/klinik/claim bulunamadı ({Tenant} / {Clinic}).", TenantName, ClinicName);
            return false;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == Email, ct);
        if (user is null)
        {
            user = new User(Email, hasher.Hash(Password));
            user.SetDisplayName(DisplayName);
            user.ConfirmEmail();
            await db.Users.AddAsync(user, ct);
            await db.SaveChangesAsync(ct);
            logger?.LogInformation("Test hekim kullanıcısı oluşturuldu.");
        }

        var tenantLink = await db.UserTenants.FirstOrDefaultAsync(x => x.UserId == user.Id, ct);
        if (tenantLink is null)
        {
            await db.UserTenants.AddAsync(new UserTenant(user.Id, tenant.Id), ct);
        }
        else if (tenantLink.TenantId != tenant.Id)
        {
            logger?.LogWarning("Test hekim başka bir kiracıya bağlı; seed atlandı (tek kiracı kuralı korunur).");
            return false;
        }

        if (!await db.UserOperationClaims.AnyAsync(x => x.UserId == user.Id && x.OperationClaimId == claim.Id, ct))
            await db.UserOperationClaims.AddAsync(new UserOperationClaim(user.Id, claim.Id), ct);

        if (!await db.UserClinics.AnyAsync(x => x.UserId == user.Id && x.ClinicId == clinic.Id, ct))
            await db.UserClinics.AddAsync(new UserClinic(user.Id, clinic.Id), ct);

        await db.SaveChangesAsync(ct);
        return true;
    }
}
