using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Tenants;

/// <summary>
/// Tam test 2026-11-22 kararı: klinik yöneticisi (ClinicAdmin) kendi kliniğine personel davet eder; başka kliniğe ve işletme yöneticisi
/// (Admin) rolüyle davet edemez, kurum ayarlarını değiştiremez (<see cref="TenantMemberManagementAccess"/>).
/// </summary>
public sealed class ClinicAdminInviteIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ClinicAdminInviteIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ClinicAdmin_Should_Invite_Only_To_Own_Clinic_And_Not_As_Tenant_Admin()
    {
        Guid tenantId, ownClinicId, otherClinicId, vetClaimId, adminClaimId;
        var email = $"clinic-admin-{Guid.NewGuid():N}@example.com";
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var now = DateTime.UtcNow;
            var tenant = new Tenant($"KY Kiracı {Guid.NewGuid():N}"[..24], now);
            var own = new Clinic(tenant.Id, "Merkez", "İzmir");
            var other = new Clinic(tenant.Id, "Şube", "Bursa");
            var user = new User(email, hasher.Hash("123456"), now);
            db.AddRange(tenant, own, other, user);
            db.UserTenants.Add(new UserTenant(user.Id, tenant.Id, now));
            db.UserClinics.Add(new UserClinic(user.Id, own.Id, now));
            db.UserOperationClaims.Add(new UserOperationClaim(user.Id, (await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin")).Id));
            db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Premium, now, 14));
            await db.SaveChangesAsync();
            (tenantId, ownClinicId, otherClinicId) = (tenant.Id, own.Id, other.Id);
            vetClaimId = (await db.OperationClaims.SingleAsync(c => c.Name == "Veteriner")).Id;
            adminClaimId = (await db.OperationClaims.SingleAsync(c => c.Name == "Admin")).Id;
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, [PermissionCatalog.Tenants.InviteCreate]);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Task<HttpResponseMessage> InviteAsync(Guid clinicId, Guid claimId) => _client.PostAsJsonAsync(
            $"/api/v1/tenants/{tenantId}/invites", new { email = $"ky-{Guid.NewGuid():N}@example.com", clinicId, operationClaimId = claimId });

        (await InviteAsync(ownClinicId, vetClaimId)).StatusCode.Should().Be(HttpStatusCode.OK);

        var otherClinic = await InviteAsync(otherClinicId, vetClaimId);
        otherClinic.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(otherClinic)).Should().Be(TenantMemberManagementAccess.ClinicAccessDeniedCode);

        var asAdmin = await InviteAsync(ownClinicId, adminClaimId);
        asAdmin.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(asAdmin)).Should().Be(TenantMemberManagementAccess.RoleNotAssignableCode);

        var roles = await _client.GetFromJsonAsync<List<AssignableRole>>($"/api/v1/tenants/{tenantId}/assignable-operation-claims");
        roles!.Select(r => r.OperationClaimName).Should().NotContain("Admin").And.Contain("Veteriner");

        var settings = await _client.PutAsJsonAsync($"/api/v1/tenants/{tenantId}/settings", new { name = $"Yeni Ad {Guid.NewGuid():N}"[..20] });
        settings.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private sealed record AssignableRole(Guid OperationClaimId, string OperationClaimName);
}
