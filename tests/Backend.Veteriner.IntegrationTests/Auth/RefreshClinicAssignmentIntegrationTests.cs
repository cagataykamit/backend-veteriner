using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Auth;

/// <summary>
/// Yenileme, oturumdaki kliniği yalnız kullanıcı o kliniğe hâlâ atanmışsa (ya da tenant-wide rolse) token'a taşır;
/// atama kaldırıldıysa <c>Auth.ClinicSelectionRequired</c> döner (select-clinic ile aynı atama kuralı).
/// </summary>
public sealed class RefreshClinicAssignmentIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public RefreshClinicAssignmentIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Refresh_Should_Keep_Clinic_When_Assignment_Still_Exists()
    {
        var (tenantId, clinicId) = await PrepareAsync();
        var email = await SeedMemberAsync(tenantId, "Sekreter", clinicId);
        var refreshToken = await LoginAndSelectClinicAsync(email, clinicId);

        var refreshed = await RefreshAsync(refreshToken);

        refreshed.StatusCode.Should().Be(HttpStatusCode.OK, await refreshed.Content.ReadAsStringAsync());
        ClinicIdOf(await AccessTokenOfAsync(refreshed)).Should().Be(clinicId.ToString("D"));
    }

    [Fact]
    public async Task Refresh_Should_Require_ClinicSelection_When_Assignment_Removed()
    {
        var (tenantId, clinicId) = await PrepareAsync();
        var email = await SeedMemberAsync(tenantId, "Sekreter", clinicId);
        var refreshToken = await LoginAndSelectClinicAsync(email, clinicId);
        await RemoveAssignmentAsync(email, clinicId);

        var refreshed = await RefreshAsync(refreshToken);

        refreshed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(refreshed)).Should().Be("Auth.ClinicSelectionRequired");
    }

    [Fact]
    public async Task Refresh_Should_Keep_Clinic_For_TenantWide_Role_Without_Assignment()
    {
        var (tenantId, clinicId) = await PrepareAsync();
        var email = await SeedMemberAsync(tenantId, "Admin", clinicId: null);
        var refreshToken = await LoginAndSelectClinicAsync(email, clinicId);

        var refreshed = await RefreshAsync(refreshToken);

        refreshed.StatusCode.Should().Be(HttpStatusCode.OK, await refreshed.Content.ReadAsStringAsync());
        ClinicIdOf(await AccessTokenOfAsync(refreshed)).Should().Be(clinicId.ToString("D"));
    }

    private async Task<(Guid TenantId, Guid ClinicId)> PrepareAsync()
    {
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
        var clinic = new Clinic(tenant.Id, $"Refresh-{Guid.NewGuid():N}"[..15], "Izmir");
        db.Clinics.Add(clinic);
        await db.SaveChangesAsync();
        return (tenant.Id, clinic.Id);
    }

    private async Task<string> SeedMemberAsync(Guid tenantId, string claimName, Guid? clinicId)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var claimId = await db.OperationClaims.Where(c => c.Name == claimName).Select(c => c.Id).SingleAsync();

        var user = new User($"refresh-clinic-{Guid.NewGuid():N}@example.com", hasher.Hash("123456"), DateTime.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claimId));
        db.UserTenants.Add(new UserTenant(user.Id, tenantId, DateTime.UtcNow));
        if (clinicId is { } cid)
            db.UserClinics.Add(new UserClinic(user.Id, cid, DateTime.UtcNow));
        await db.SaveChangesAsync();
        return user.Email;
    }

    private async Task RemoveAssignmentAsync(string email, Guid clinicId)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        await db.UserClinics.Where(uc => uc.UserId == userId && uc.ClinicId == clinicId).ExecuteDeleteAsync();
    }

    private async Task<string> LoginAndSelectClinicAsync(string email, Guid clinicId)
    {
        var client = _factory.CreateClient();
        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, "123456");
        var selected = await client.PostAsJsonAsync("/api/v1/auth/select-clinic",
            new { RefreshToken = login.RefreshToken, ClinicId = clinicId });
        selected.StatusCode.Should().Be(HttpStatusCode.OK, await selected.Content.ReadAsStringAsync());

        var json = await selected.Content.ReadFromJsonAsync<JsonElement>();
        ClinicIdOf(json.GetProperty("accessToken").GetString()!).Should().Be(clinicId.ToString("D"));
        return json.GetProperty("refreshToken").GetString()!;
    }

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken)
        => _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/refresh", new { RefreshToken = refreshToken });

    private static async Task<string> AccessTokenOfAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

    private static string? ClinicIdOf(string accessToken)
        => new JwtSecurityTokenHandler().ReadJwtToken(accessToken).Claims
            .FirstOrDefault(c => c.Type == VeterinerClaims.ClinicId)?.Value;
}
