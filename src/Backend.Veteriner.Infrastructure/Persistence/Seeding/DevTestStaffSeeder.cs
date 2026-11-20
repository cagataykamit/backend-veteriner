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
/// YALNIZCA geliştirme ortamı: ekranları rol rol denemek için her klinik rolünden bir test kullanıcısı (hekim, sekreter,
/// klinik yöneticisi, işletme yöneticisi). Kullanıcılar mevcut bir kiracıdaki mevcut bir kliniğe atanır (kiracı/klinik burada
/// oluşturulmaz); e-posta doğrulanmış, davet e-postası gerektirmez. Idempotent: tekrar çalıştırma kopya üretmez. Parola gerçek
/// bir parola değildir, yalnızca bu dosyada tanımlı yerel test değeridir. Üretim/staging'de çalışmaz (ortam kontrolü) ve
/// normal <c>seed</c> hattına dahil değildir; ayrı <c>seed-dev-staff</c> komutuyla çalışır.
/// </summary>
public static class DevTestStaffSeeder
{
    public sealed record Account(string Email, string DisplayName, string ClaimName);

    public const string TenantName = "YağmurVet";
    public const string ClinicName = "Merkez Pati";

    public static readonly Account Veterinarian = new("dr.test@example.com", "Dr. Test Hekim", "Veteriner");
    public static readonly Account Secretary = new("sekreter.test@example.com", "Test Sekreter", "Sekreter");
    public static readonly Account ClinicAdmin = new("klinik-yonetici.test@example.com", "Test Klinik Yöneticisi", "ClinicAdmin");
    public static readonly Account TenantAdmin = new("isletme-yonetici.test@example.com", "Test İşletme Yöneticisi", "Admin");

    public static readonly IReadOnlyList<Account> Accounts = [Veterinarian, Secretary, ClinicAdmin, TenantAdmin];

    /// <summary>Yerel test değeri; hiçbir gerçek hesapta kullanılmaz.</summary>
    public const string Password = "Test-Hekim-4f9Kq2";

    /// <returns>Tüm hesaplar hazırsa true; kiracı/klinik/claim bulunamadığı ya da bir hesap başka kiracıya bağlı olduğu için atlandıysa false.</returns>
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
                "Test personel seed'i yalnızca Development ortamında çalışır.");
        }

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Name == TenantName, ct);
        var clinic = tenant is null
            ? null
            : await db.Clinics.FirstOrDefaultAsync(
                c => c.TenantId == tenant.Id && c.Name == ClinicName && c.IsActive, ct);
        var claimNames = Accounts.Select(a => a.ClaimName).ToList();
        var claims = await db.OperationClaims.Where(c => claimNames.Contains(c.Name)).ToDictionaryAsync(c => c.Name, ct);
        if (tenant is null || clinic is null || claims.Count != claimNames.Distinct().Count())
        {
            logger?.LogWarning(
                "Test personel seed atlandı: kiracı/klinik/claim bulunamadı ({Tenant} / {Clinic}).", TenantName, ClinicName);
            return false;
        }

        var allReady = true;
        foreach (var account in Accounts)
            allReady &= await SeedAccountAsync(db, hasher, account, tenant, clinic, claims[account.ClaimName], logger, ct);

        return allReady;
    }

    private static async Task<bool> SeedAccountAsync(
        AppDbContext db,
        IPasswordHasher hasher,
        Account account,
        Tenant tenant,
        Clinic clinic,
        OperationClaim claim,
        ILogger? logger,
        CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == account.Email, ct);
        var utcNow = TimeProvider.System.GetUtcNow().UtcDateTime;
        if (user is null)
        {
            user = new User(account.Email, hasher.Hash(Password), utcNow);
            user.SetDisplayName(account.DisplayName, utcNow);
            user.ConfirmEmail(utcNow);
            await db.Users.AddAsync(user, ct);
            await db.SaveChangesAsync(ct);
            logger?.LogInformation("Test kullanıcısı oluşturuldu ({Role}).", account.ClaimName);
        }

        var tenantLink = await db.UserTenants.FirstOrDefaultAsync(x => x.UserId == user.Id, ct);
        if (tenantLink is null)
        {
            await db.UserTenants.AddAsync(new UserTenant(user.Id, tenant.Id, utcNow), ct);
        }
        else if (tenantLink.TenantId != tenant.Id)
        {
            logger?.LogWarning(
                "Test kullanıcısı ({Role}) başka bir kiracıya bağlı; atlandı (tek kiracı kuralı korunur).", account.ClaimName);
            return false;
        }

        if (!await db.UserOperationClaims.AnyAsync(x => x.UserId == user.Id && x.OperationClaimId == claim.Id, ct))
            await db.UserOperationClaims.AddAsync(new UserOperationClaim(user.Id, claim.Id), ct);

        if (!await db.UserClinics.AnyAsync(x => x.UserId == user.Id && x.ClinicId == clinic.Id, ct))
            await db.UserClinics.AddAsync(new UserClinic(user.Id, clinic.Id, utcNow), ct);

        await db.SaveChangesAsync(ct);
        return true;
    }
}
