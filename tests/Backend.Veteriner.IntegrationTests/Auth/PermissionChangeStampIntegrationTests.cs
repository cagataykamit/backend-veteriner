using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
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
/// SEC-002: erişimi değişen kullanıcının eski erişim token'ı 401 alır; yenilenmiş token güncel izinle çalışır.
/// Damga saniye çözünürlüğünde olduğu için değişiklikten önce saniye sınırı beklenir.
/// </summary>
public sealed class PermissionChangeStampIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string ProbeUrl = "/api/v1/me/sessions";

    private readonly CustomWebApplicationFactory _factory;

    public PermissionChangeStampIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task RemoveMemberRole_Should_Reject_OldToken_And_Accept_RefreshedToken()
    {
        var (tenantId, _) = await PrepareAsync();
        var member = await SeedMemberAsync(tenantId, "Sekreter");
        var bystander = await SeedMemberAsync(tenantId, "Sekreter");
        var admin = await LoginAsync(AdminClaimSeeder.PlatformAdminUserEmail);
        var memberTokens = await LoginAsync(member.Email);
        var bystanderTokens = await LoginAsync(bystander.Email);
        PermissionsOf(memberTokens.AccessToken).Should().NotBeEmpty();

        await WaitForNextSecondAsync();
        var remove = await SendAsync(HttpMethod.Delete,
            $"/api/v1/tenants/{tenantId}/members/{member.UserId}/roles/{member.ClaimId}", admin.AccessToken);
        remove.StatusCode.Should().Be(HttpStatusCode.OK, await remove.Content.ReadAsStringAsync());

        var stale = await SendAsync(HttpMethod.Get, ProbeUrl, memberTokens.AccessToken);
        stale.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        stale.Headers.WwwAuthenticate.ToString().Should().Contain("invalid_token");
        (await ProbeAsync(bystanderTokens.AccessToken)).Should().Be(HttpStatusCode.OK);

        var refreshed = await RefreshAsync(memberTokens.RefreshToken);
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        var newAccess = (await refreshed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        (await ProbeAsync(newAccess)).Should().Be(HttpStatusCode.OK);
        PermissionsOf(newAccess).Should().BeEmpty();
    }

    [Fact]
    public async Task RemovePermissionFromRole_Should_Reject_OldTokens_Of_Role_Users_Only()
    {
        var (tenantId, _) = await PrepareAsync();
        var (claimId, permissionId) = await SeedRoleWithPermissionAsync(PermissionCatalog.Clients.Read);
        var first = await SeedMemberAsync(tenantId, claimId);
        var second = await SeedMemberAsync(tenantId, claimId);
        var bystander = await SeedMemberAsync(tenantId, "Sekreter");
        var admin = await LoginAsync(AdminClaimSeeder.PlatformAdminUserEmail);
        var firstTokens = await LoginAsync(first.Email);
        var secondTokens = await LoginAsync(second.Email);
        var bystanderTokens = await LoginAsync(bystander.Email);
        PermissionsOf(firstTokens.AccessToken).Should().Contain(PermissionCatalog.Clients.Read);

        await WaitForNextSecondAsync();
        var remove = await SendAsync(HttpMethod.Delete,
            $"/api/v1/admin/claims/{claimId}/permissions/{permissionId}", admin.AccessToken);
        remove.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ProbeAsync(firstTokens.AccessToken)).Should().Be(HttpStatusCode.Unauthorized);
        (await ProbeAsync(secondTokens.AccessToken)).Should().Be(HttpStatusCode.Unauthorized);
        (await ProbeAsync(bystanderTokens.AccessToken)).Should().Be(HttpStatusCode.OK);

        var refreshed = await RefreshAsync(firstTokens.RefreshToken);
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        var newAccess = (await refreshed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        PermissionsOf(newAccess).Should().NotContain(PermissionCatalog.Clients.Read);
    }

    [Fact]
    public async Task RemoveMember_Should_Reject_OldToken_And_Refresh()
    {
        var (tenantId, _) = await PrepareAsync();
        // Son yönetici koruması (TenantMembers.CannotRemoveLastAdmin) çıkarmayı engellemesin.
        await SeedMemberAsync(tenantId, "Admin");
        var member = await SeedMemberAsync(tenantId, "Sekreter");
        var admin = await LoginAsync(AdminClaimSeeder.PlatformAdminUserEmail);
        var memberTokens = await LoginAsync(member.Email);

        await WaitForNextSecondAsync();
        var remove = await SendAsync(HttpMethod.Delete,
            $"/api/v1/tenants/{tenantId}/members/{member.UserId}", admin.AccessToken);
        remove.StatusCode.Should().Be(HttpStatusCode.OK, await remove.Content.ReadAsStringAsync());

        (await ProbeAsync(memberTokens.AccessToken)).Should().Be(HttpStatusCode.Unauthorized);
        (await RefreshAsync(memberTokens.RefreshToken)).IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveMemberClinic_Should_Reject_OldToken_And_Accept_RefreshedToken()
    {
        var (tenantId, clinicId) = await PrepareAsync();
        var member = await SeedMemberAsync(tenantId, "Sekreter", clinicId);
        var admin = await LoginAsync(AdminClaimSeeder.PlatformAdminUserEmail);
        var memberTokens = await LoginAsync(member.Email);

        await WaitForNextSecondAsync();
        var remove = await SendAsync(HttpMethod.Delete,
            $"/api/v1/tenants/{tenantId}/members/{member.UserId}/clinics/{clinicId}", admin.AccessToken);
        remove.StatusCode.Should().Be(HttpStatusCode.OK, await remove.Content.ReadAsStringAsync());

        (await ProbeAsync(memberTokens.AccessToken)).Should().Be(HttpStatusCode.Unauthorized);

        var refreshed = await RefreshAsync(memberTokens.RefreshToken);
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        var newAccess = (await refreshed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        (await ProbeAsync(newAccess)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_Should_Issue_AccessToken_With_30_Minute_Lifetime()
    {
        var tokens = await LoginAsync(AdminClaimSeeder.PlatformAdminUserEmail);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(tokens.AccessToken);
        var iat = long.Parse(jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Iat).Value);
        var exp = new DateTimeOffset(jwt.ValidTo).ToUnixTimeSeconds();

        (exp - iat).Should().Be(30 * 60);
    }

    private sealed record SeededMember(Guid UserId, string Email, Guid ClaimId);

    private sealed record Tokens(string AccessToken, string RefreshToken);

    private async Task<(Guid TenantId, Guid ClinicId)> PrepareAsync()
    {
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
        var clinic = new Clinic(tenant.Id, $"Sec002-{Guid.NewGuid():N}"[..15], "Izmir");
        db.Clinics.Add(clinic);
        await db.SaveChangesAsync();
        return (tenant.Id, clinic.Id);
    }

    private async Task<SeededMember> SeedMemberAsync(Guid tenantId, string claimName, Guid? clinicId = null)
    {
        Guid claimId;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            claimId = await db.OperationClaims.Where(c => c.Name == claimName).Select(c => c.Id).SingleAsync();
        }

        return await SeedMemberAsync(tenantId, claimId, clinicId);
    }

    private async Task<SeededMember> SeedMemberAsync(Guid tenantId, Guid claimId, Guid? clinicId = null)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var user = new User($"sec002-{Guid.NewGuid():N}@example.com", hasher.Hash("123456"), DateTime.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claimId));
        db.UserTenants.Add(new UserTenant(user.Id, tenantId, DateTime.UtcNow));
        if (clinicId is { } cid)
            db.UserClinics.Add(new UserClinic(user.Id, cid, DateTime.UtcNow));
        await db.SaveChangesAsync();

        return new SeededMember(user.Id, user.Email, claimId);
    }

    private async Task<(Guid ClaimId, Guid PermissionId)> SeedRoleWithPermissionAsync(string permissionCode)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var permissionId = await db.Permissions.Where(p => p.Code == permissionCode).Select(p => p.Id).SingleAsync();
        var claim = new OperationClaim($"Sec002-{Guid.NewGuid():N}");
        db.OperationClaims.Add(claim);
        await db.SaveChangesAsync();

        db.OperationClaimPermissions.Add(new OperationClaimPermission(claim.Id, permissionId));
        await db.SaveChangesAsync();
        return (claim.Id, permissionId);
    }

    private async Task<Tokens> LoginAsync(string email)
    {
        var login = await IntegrationTestAuthHelper.LoginAsync(_factory.CreateClient(), _factory.Services, email, "123456");
        return new Tokens(login.AccessToken, login.RefreshToken);
    }

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken)
        => _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/refresh", new { RefreshToken = refreshToken });

    private async Task<HttpStatusCode> ProbeAsync(string accessToken)
        => (await SendAsync(HttpMethod.Get, ProbeUrl, accessToken)).StatusCode;

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string accessToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return _factory.CreateClient().SendAsync(request);
    }

    private static IReadOnlyList<string> PermissionsOf(string accessToken)
        => new JwtSecurityTokenHandler().ReadJwtToken(accessToken).Claims
            .Where(c => c.Type == "permission")
            .Select(c => c.Value)
            .ToList();

    private static async Task WaitForNextSecondAsync()
    {
        var second = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        while (DateTimeOffset.UtcNow.ToUnixTimeSeconds() == second)
            await Task.Delay(50);
    }
}
