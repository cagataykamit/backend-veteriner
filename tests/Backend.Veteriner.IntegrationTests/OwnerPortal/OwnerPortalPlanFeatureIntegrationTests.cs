using System.Net;
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
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.OwnerPortal;

/// <summary>
/// TIER-001 (docs/PLAN_FEATURES_API_CONTRACT.md): paket çok kliniği gerçekten zorlar; portal 2026-11-16 kullanıcı kararıyla Temel dahil
/// her pakette açık (ADR-035). Deneme seçilen pakete göre davranır, çok klinik dahil.
/// </summary>
public sealed partial class OwnerPortalIntegrationTests
{
    private const string FeatureNotInPlan = "Subscriptions.FeatureNotInPlan";

    private static readonly string[] PlanTestPermissions =
    [
        PermissionCatalog.OwnerPortal.Manage,
        PermissionCatalog.Clinics.Read,
        PermissionCatalog.Clinics.Create,
        PermissionCatalog.Clinics.Update,
    ];

    private async Task<StaffContext> SeedPlanTenantAsync(SubscriptionPlanCode plan, bool trialing = false)
    {
        var email = $"plan-owner-{Guid.NewGuid():N}@example.com";
        Guid tenantId, clinicId;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var now = DateTime.UtcNow;
            var tenant = new Tenant($"Paket-{Guid.NewGuid():N}"[..20], now);
            var clinic = new Clinic(tenant.Id, "Merkez", "İzmir");
            var subscription = TenantSubscription.StartTrial(tenant.Id, plan, now, 14);
            if (!trialing)
                subscription.ActivatePaidPlan(plan, now);
            var user = new User(email, hasher.Hash(Password), now);
            var adminClaim = await db.OperationClaims.SingleAsync(c => c.Name == "Admin");
            db.AddRange(tenant, clinic, subscription, user, new UserTenant(user.Id, tenant.Id, now), new UserOperationClaim(user.Id, adminClaim.Id));
            await db.SaveChangesAsync();
            tenantId = tenant.Id;
            clinicId = clinic.Id;
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, PlanTestPermissions, clinicId);
        return new StaffContext(token, tenantId, clinicId);
    }

    private async Task ChangePlanAsync(Guid tenantId, SubscriptionPlanCode plan)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var subscription = await db.TenantSubscriptions.SingleAsync(s => s.TenantId == tenantId);
        subscription.ActivatePaidPlan(plan, DateTime.UtcNow);
        await db.SaveChangesAsync();
    }

    private Task<HttpResponseMessage> CreateClinicAsync(StaffContext staff, string name)
        => SendAsync(HttpMethod.Post, "/api/v1/clinics", staff.Token, new { name, city = "Bursa" });

    private static async Task ShouldBeNotInPlanAsync(HttpResponseMessage response, string requiredPlanCode)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be(FeatureNotInPlan);
        json.GetProperty("requiredPlanCode").GetString().Should().Be(requiredPlanCode);
    }

    [Theory]
    [InlineData(SubscriptionPlanCode.Basic, false)]
    [InlineData(SubscriptionPlanCode.Basic, true)]
    [InlineData(SubscriptionPlanCode.Pro, false)]
    [InlineData(SubscriptionPlanCode.Premium, false)]
    public async Task Every_Plan_Should_Allow_Portal_Invite_And_Accept(SubscriptionPlanCode plan, bool trialing)
    {
        var staff = await SeedPlanTenantAsync(plan, trialing);
        var client = await SeedClientAsync(staff.TenantId);

        var access = await (await SendAsync(HttpMethod.Get, $"/api/v1/clients/{client.Id}/portal-access", staff.Token))
            .Content.ReadFromJsonAsync<JsonElement>();
        access.GetProperty("canInvite").GetBoolean().Should().BeTrue();

        var session = await InviteAndAcceptAsync(staff, client: client);

        (await SendAsync(HttpMethod.Get, $"/api/portal/v1/links/{session.LinkId}/pets", session.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>2026-11-16: Temel'e inmek portalı kapatmaz; mevcut bağ, bekleyen davet ve yeni davet açık kalır.</summary>
    [Fact]
    public async Task Downgrade_To_Basic_Should_Keep_Portal_Open()
    {
        var staff = await SeedPlanTenantAsync(SubscriptionPlanCode.Pro);
        var session = await InviteAndAcceptAsync(staff);
        var pendingClient = await SeedClientAsync(staff.TenantId);
        var pendingToken = await InviteAsync(staff, pendingClient.Id);

        await ChangePlanAsync(staff.TenantId, SubscriptionPlanCode.Basic);

        foreach (var path in new[] { "pets", "appointments", "balance", "clinic" })
        {
            (await SendAsync(HttpMethod.Get, $"/api/portal/v1/links/{session.LinkId}/{path}", session.AccessToken))
                .StatusCode.Should().Be(HttpStatusCode.OK, path);
        }

        var me = await (await SendAsync(HttpMethod.Get, "/api/portal/v1/me", session.AccessToken)).Content.ReadFromJsonAsync<JsonElement>();
        me.GetProperty("links").EnumerateArray().Single().GetProperty("isPortalServiceAvailable").GetBoolean().Should().BeTrue();
        (await SendAsync(HttpMethod.Get, $"/api/portal/v1/invites/{pendingToken}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var newSession = await InviteAndAcceptAsync(staff);
        (await SendAsync(HttpMethod.Get, $"/api/portal/v1/links/{newSession.LinkId}/pets", newSession.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(SubscriptionPlanCode.Basic)]
    [InlineData(SubscriptionPlanCode.Pro)]
    public async Task Paid_Plan_Without_MultiClinic_Should_Reject_Second_Clinic(SubscriptionPlanCode plan)
    {
        var staff = await SeedPlanTenantAsync(plan);

        await ShouldBeNotInPlanAsync(await CreateClinicAsync(staff, $"Şube-{Guid.NewGuid():N}"[..12]), "Premium");
    }

    [Fact]
    public async Task Premium_Should_Allow_Second_Clinic()
    {
        var staff = await SeedPlanTenantAsync(SubscriptionPlanCode.Premium);

        (await CreateClinicAsync(staff, $"Şube-{Guid.NewGuid():N}"[..12])).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>ADR-035 2026-11-08: deneme seçilen pakete göre davranır, çok klinik dahil.</summary>
    [Theory]
    [InlineData(SubscriptionPlanCode.Basic)]
    [InlineData(SubscriptionPlanCode.Pro)]
    public async Task Trial_Without_MultiClinic_Should_Reject_Second_Clinic(SubscriptionPlanCode plan)
    {
        var staff = await SeedPlanTenantAsync(plan, trialing: true);

        await ShouldBeNotInPlanAsync(await CreateClinicAsync(staff, $"Şube-{Guid.NewGuid():N}"[..12]), "Premium");

        var state = await (await SendAsync(HttpMethod.Get, $"/api/v1/tenants/{staff.TenantId}/access-state", staff.Token))
            .Content.ReadFromJsonAsync<JsonElement>();
        state.GetProperty("planFeatures").EnumerateArray().Single(f => f.GetProperty("feature").GetString() == "MultiClinic")
            .GetProperty("isEnabled").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Premium_Trial_Should_Allow_Second_Clinic()
    {
        var staff = await SeedPlanTenantAsync(SubscriptionPlanCode.Premium, trialing: true);

        (await CreateClinicAsync(staff, $"Şube-{Guid.NewGuid():N}"[..12])).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>Kararın öncesinden denemede iki aktif kliniği olan kiracı: klinikler kalır, yenisi açılamaz.</summary>
    [Fact]
    public async Task Existing_Trial_With_Two_Clinics_Should_Keep_Them_And_Block_New_Ones()
    {
        var staff = await SeedPlanTenantAsync(SubscriptionPlanCode.Basic, trialing: true);
        Guid secondClinicId;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var second = new Clinic(staff.TenantId, "Eski Şube", "Bursa");
            db.Clinics.Add(second);
            await db.SaveChangesAsync();
            secondClinicId = second.Id;
        }

        var detail = await SendAsync(HttpMethod.Get, $"/api/v1/clinics/{secondClinicId}", staff.Token);
        detail.StatusCode.Should().Be(HttpStatusCode.OK);
        (await detail.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isActive").GetBoolean().Should().BeTrue();
        await ShouldBeNotInPlanAsync(await CreateClinicAsync(staff, $"Üçüncü-{Guid.NewGuid():N}"[..12]), "Premium");
    }

    [Fact]
    public async Task Downgrade_Should_Keep_Excess_Clinics_Working_And_Block_New_Ones()
    {
        var staff = await SeedPlanTenantAsync(SubscriptionPlanCode.Premium);
        var created = await CreateClinicAsync(staff, $"Şube-{Guid.NewGuid():N}"[..12]);
        var secondClinicId = await created.Content.ReadFromJsonAsync<Guid>();

        await ChangePlanAsync(staff.TenantId, SubscriptionPlanCode.Basic);

        var detail = await SendAsync(HttpMethod.Get, $"/api/v1/clinics/{secondClinicId}", staff.Token);
        detail.StatusCode.Should().Be(HttpStatusCode.OK);
        (await detail.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isActive").GetBoolean().Should().BeTrue();

        await ShouldBeNotInPlanAsync(await CreateClinicAsync(staff, $"Üçüncü-{Guid.NewGuid():N}"[..12]), "Premium");

        // Pasife alınan klinik, başka aktif klinik varken geri açılamaz; pasif kayıt silinmez.
        (await SendAsync(HttpMethod.Post, $"/api/v1/clinics/{secondClinicId}/deactivate", staff.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        await ShouldBeNotInPlanAsync(await SendAsync(HttpMethod.Post, $"/api/v1/clinics/{secondClinicId}/activate", staff.Token), "Premium");
    }

    [Fact]
    public async Task Portal_Settings_Should_Be_Writable_On_Basic()
    {
        var staff = await SeedPlanTenantAsync(SubscriptionPlanCode.Basic);

        (await SendAsync(HttpMethod.Put, $"/api/v1/clinics/{staff.ClinicId}/portal-settings", staff.Token, new { isEnabled = false }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AccessState_Should_Report_Plan_Features()
    {
        var staff = await SeedPlanTenantAsync(SubscriptionPlanCode.Basic);

        var response = await SendAsync(HttpMethod.Get, $"/api/v1/tenants/{staff.TenantId}/access-state", staff.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("planCode").GetString().Should().Be("Basic");
        var features = json.GetProperty("planFeatures").EnumerateArray()
            .ToDictionary(f => f.GetProperty("feature").GetString()!, f => f);
        features["OwnerPortal"].GetProperty("isEnabled").GetBoolean().Should().BeTrue();
        features["OwnerPortal"].GetProperty("requiredPlanCode").GetString().Should().Be("Basic");
        features["PortalAppointmentRequests"].GetProperty("isEnabled").GetBoolean().Should().BeTrue();
        // 2026-11-23: kamerayla barkod okutma tüm paketlerde (ADR-035).
        features["MobileBarcode"].GetProperty("isEnabled").GetBoolean().Should().BeTrue();
        features["MobileBarcode"].GetProperty("requiredPlanCode").GetString().Should().Be("Basic");
        features["Waitlist"].GetProperty("isEnabled").GetBoolean().Should().BeFalse();
        features["MultiClinic"].GetProperty("isEnabled").GetBoolean().Should().BeFalse();
        features["MultiClinic"].GetProperty("requiredPlanCode").GetString().Should().Be("Premium");
    }
}
