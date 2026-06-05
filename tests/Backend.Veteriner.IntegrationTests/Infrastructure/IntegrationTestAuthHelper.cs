using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Infrastructure;

internal sealed record IntegrationLoginTokens(
    string AccessToken,
    string RefreshToken,
    Guid UserId,
    Guid TenantId);

internal static class IntegrationTestAuthHelper
{
    public static async Task EnsureRolePermissionBindingsAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await RolePermissionBindingSeeder.SeedAsync(db);
    }

    public static async Task<IntegrationLoginTokens> LoginAsync(HttpClient client, IServiceProvider services, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { Email = email, Password = password });
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = json.GetProperty("accessToken").GetString()!;
        var refreshToken = json.GetProperty("refreshToken").GetString()!;
        var tenantId = json.GetProperty("resolvedTenantId").GetGuid();

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await db.Users
            .Where(u => u.Email == email)
            .Select(u => u.Id)
            .SingleAsync();

        return new IntegrationLoginTokens(accessToken, refreshToken, userId, tenantId);
    }

    /// <summary>
    /// Tenant Admin claim'li kullanıcı; tenant-wide /me/clinics ve Clinics.Create smoke için.
    /// </summary>
    public static async Task<(string Email, string Password, Guid ExtraClinicId)> SeedTenantAdminUserAsync(
        IServiceProvider services,
        IPasswordHasher hasher)
    {
        await EnsureRolePermissionBindingsAsync(services);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
        var adminClaim = await db.OperationClaims.SingleAsync(c => c.Name == "Admin");

        var email = $"tenant-admin-{Guid.NewGuid():N}@example.com";
        const string password = "123456";
        var user = new User(email, hasher.Hash(password));
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserOperationClaims.Add(new UserOperationClaim(user.Id, adminClaim.Id));
        db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));
        await db.SaveChangesAsync();

        var extraClinic = new Clinic(tenant.Id, $"Extra-{Guid.NewGuid():N}"[..14], "Ankara");
        db.Clinics.Add(extraClinic);
        await db.SaveChangesAsync();

        return (email, password, extraClinic.Id);
    }

    /// <summary>
    /// ClinicAdmin claim'li kullanıcı; yalnızca atanmış kliniği görür, Clinics.Create yoktur.
    /// </summary>
    public static async Task<(string Email, string Password, Guid AssignedClinicId, Guid UnassignedClinicId)>
        SeedClinicAdminUserAsync(IServiceProvider services, IPasswordHasher hasher)
    {
        await EnsureRolePermissionBindingsAsync(services);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
        var assignedClinic = await db.Clinics.SingleAsync(c =>
            c.TenantId == tenant.Id && c.Name == DataSeeder.DefaultSeedClinicName);

        var unassignedClinic = new Clinic(tenant.Id, $"Hidden-{Guid.NewGuid():N}"[..14], "İzmir");
        db.Clinics.Add(unassignedClinic);

        var clinicAdminClaim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
        var email = $"clinicadmin-{Guid.NewGuid():N}@example.com";
        const string password = "123456";
        var user = new User(email, hasher.Hash(password));
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserOperationClaims.Add(new UserOperationClaim(user.Id, clinicAdminClaim.Id));
        db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));
        db.UserClinics.Add(new UserClinic(user.Id, assignedClinic.Id));
        await db.SaveChangesAsync();

        return (email, password, assignedClinic.Id, unassignedClinic.Id);
    }

    /// <summary>
    /// Read-only tenant için JWT (Products smoke ile aynı desen).
    /// </summary>
    public static async Task<string> IssueReadOnlyTenantTokenAsync(
        IServiceProvider services,
        IJwtTokenService jwt,
        IReadOnlyCollection<string> permissions)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = new Tenant($"RO-{Guid.NewGuid():N}"[..16]);
        db.Tenants.Add(tenant);

        var now = DateTime.UtcNow;
        var subscription = TenantSubscription.StartTrial(
            tenant.Id,
            SubscriptionPlanCode.Basic,
            now.AddDays(-40),
            trialDays: 7);
        db.TenantSubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        var claims = permissions
            .Select(p => new Claim("permission", p))
            .Append(new Claim(VeterinerClaims.TenantId, tenant.Id.ToString("D")))
            .ToList();

        var (accessToken, _, _) = jwt.Create(
            Guid.NewGuid(),
            $"ro-{Guid.NewGuid():N}@example.com",
            Array.Empty<string>(),
            claims);

        return accessToken;
    }
}
