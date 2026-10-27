using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Validation;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Persistence.Seeding;

/// <summary>
/// Üretimde ilk PlatformAdmin hesabını güvenli biçimde oluşturur (<c>create-platform-admin</c> komutu).
/// Sabit e-posta/parola yoktur; parola çağırandan gelir ve hiçbir yere yazılmaz. Mevcut kullanıcıya dokunmaz.
/// Önce <c>seed</c> çalıştırılmış olmalıdır (PlatformAdmin claim'i ve izinleri o hatta kurulur).
/// </summary>
public static class PlatformAdminBootstrapper
{
    public sealed record Input(string Email, string Password);

    private sealed class InputValidator : AbstractValidator<Input>
    {
        public InputValidator()
        {
            RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
            RuleFor(x => x.Password).StrongPasswordRules(includeMaxLength: true);
        }
    }

    /// <exception cref="InvalidOperationException">Girdi geçersiz, ön koşul eksik veya kullanıcı zaten var (parola metni mesaja girmez).</exception>
    public static async Task CreateAsync(
        AppDbContext db,
        IPasswordHasher hasher,
        Input input,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        var email = (input.Email ?? string.Empty).Trim().ToLowerInvariant();
        var validation = new InputValidator().Validate(new Input(email, input.Password ?? string.Empty));
        if (!validation.IsValid)
            throw new InvalidOperationException(string.Join(" ", validation.Errors.Select(e => e.ErrorMessage)));

        if (!await db.OperationClaims.AnyAsync(c => c.Name == AdminClaimSeeder.PlatformAdminClaimName, ct))
            throw new InvalidOperationException("PlatformAdmin claim'i bulunamadı. Önce 'seed' komutunu çalıştırın.");

        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            throw new InvalidOperationException("Bu e-posta ile kullanıcı zaten var; mevcut hesaba dokunulmadı.");

        // Girişte tek kiracı üyeliği zorunlu; platform yöneticisi müşteri kiracısından ayrı, boş "Platform" kiracısına bağlanır
        // (klinik/abonelik oluşturulmaz; aboneliksiz kiracıda yazma komutları zaten reddedilir).
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Name == Tenant.PlatformTenantName, ct);
        var utcNow = TimeProvider.System.GetUtcNow().UtcDateTime;
        if (tenant is null)
        {
            tenant = new Tenant(Tenant.PlatformTenantName, utcNow);
            await db.Tenants.AddAsync(tenant, ct);
            await db.SaveChangesAsync(ct);
        }
        else if (await db.Clinics.AnyAsync(c => c.TenantId == tenant.Id, ct)
                 || await db.TenantSubscriptions.AnyAsync(s => s.TenantId == tenant.Id, ct))
        {
            throw new InvalidOperationException(
                $"'{Tenant.PlatformTenantName}' adlı kiracıda klinik veya abonelik var; müşteri kiracısı olabilir. Platform yöneticisi bağlanmadı.");
        }

        var user = new User(email, hasher.Hash(input.Password!), utcNow);
        user.ConfirmEmail(utcNow);
        await db.Users.AddAsync(user, ct);
        await db.UserTenants.AddAsync(new UserTenant(user.Id, tenant.Id, utcNow), ct);
        await db.SaveChangesAsync(ct);

        await AdminClaimSeeder.SeedAsync(db, ct, email);

        logger?.LogInformation("Platform admin user created (userId {UserId}).", user.Id);
    }
}
