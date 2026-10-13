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

        // Girişte tek kiracı üyeliği zorunlu; platform yöneticisi varsayılan kiracıya bağlanır (abonelik/klinik oluşturulmaz).
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Name == DataSeeder.DefaultTenantName, ct);
        if (tenant is null)
        {
            tenant = new Tenant(DataSeeder.DefaultTenantName);
            await db.Tenants.AddAsync(tenant, ct);
            await db.SaveChangesAsync(ct);
        }

        var user = new User(email, hasher.Hash(input.Password!));
        user.ConfirmEmail();
        await db.Users.AddAsync(user, ct);
        await db.UserTenants.AddAsync(new UserTenant(user.Id, tenant.Id), ct);
        await db.SaveChangesAsync(ct);

        await AdminClaimSeeder.SeedAsync(db, ct, email);

        logger?.LogInformation("Platform admin user created (userId {UserId}).", user.Id);
    }
}
