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
/// TIER-001 (docs/PLAN_FEATURES_API_CONTRACT.md): paket portalı ve çok kliniği gerçekten zorlar. Portalsız pakette mevcut bağda okuma
/// kapanır, sahip hakları açık kalır; veri silinmez (ADR-035 2026-11-08). Deneme seçilen pakete göre davranır, çok klinik dahil.
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task Basic_Should_Reject_Portal_Invite_Also_In_Trial(bool trialing)
    {
        var staff = await SeedPlanTenantAsync(SubscriptionPlanCode.Basic, trialing);
        var client = await SeedClientAsync(staff.TenantId);

        var invite = await SendAsync(HttpMethod.Post, $"/api/v1/clients/{client.Id}/portal-invites", staff.Token);
        await ShouldBeNotInPlanAsync(invite, "Pro");

        var access = await (await SendAsync(HttpMethod.Get, $"/api/v1/clients/{client.Id}/portal-access", staff.Token))
            .Content.ReadFromJsonAsync<JsonElement>();
        access.GetProperty("canInvite").GetBoolean().Should().BeFalse();
        access.GetProperty("inviteBlockedCode").GetString().Should().Be(FeatureNotInPlan);
    }

    [Fact]
    public async Task Pro_Should_Allow_Portal_Invite_And_Accept()
    {
        var staff = await SeedPlanTenantAsync(SubscriptionPlanCode.Pro);

        var session = await InviteAndAcceptAsync(staff);

        (await SendAsync(HttpMethod.Get, $"/api/portal/v1/links/{session.LinkId}/pets", session.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Downgrade_Should_Close_Reads_On_Existing_Link_But_Keep_Owner_Rights()
    {
        var staff = await SeedPlanTenantAsync(SubscriptionPlanCode.Pro);
        var session = await InviteAndAcceptAsync(staff);
        var pendingClient = await SeedClientAsync(staff.TenantId);
        var pendingToken = await InviteAsync(staff, pendingClient.Id);

        await ChangePlanAsync(staff.TenantId, SubscriptionPlanCode.Basic);

        // Okuma uçları kapalı; yanıt sahiplik kuralıyla aynı (paket bilgisi portala çıkmaz).
        foreach (var path in new[] { "pets", "appointments", "balance", "clinic" })
        {
            var read = await SendAsync(HttpMethod.Get, $"/api/portal/v1/links/{session.LinkId}/{path}", session.AccessToken);
            read.StatusCode.Should().Be(HttpStatusCode.NotFound, path);
            (await IntegrationTestProblemDetails.ReadCodeAsync(read)).Should().Be("Portal.Links.NotFound");
        }

        // Sahip hakları açık: KVKK talebi, tercihler, hesap silme kodu.
        (await SendAsync(HttpMethod.Post, $"/api/portal/v1/links/{session.LinkId}/data-requests", session.AccessToken, new { type = "Access", message = "Kayıtlarımı istiyorum" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await SendAsync(HttpMethod.Get, $"/api/portal/v1/links/{session.LinkId}/data-requests", session.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, $"/api/portal/v1/links/{session.LinkId}/notification-preferences", session.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, $"/api/portal/v1/links/{session.LinkId}/reminder-preferences", session.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Post, "/api/portal/v1/me/deletion-code", session.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var me = await (await SendAsync(HttpMethod.Get, "/api/portal/v1/me", session.AccessToken)).Content.ReadFromJsonAsync<JsonElement>();
        var link = me.GetProperty("links").EnumerateArray().Single();
        link.GetProperty("isClinicPortalEnabled").GetBoolean().Should().BeTrue();
        link.GetProperty("isPortalServiceAvailable").GetBoolean().Should().BeFalse();

        // Veri silinmez: yükseltince okuma geri gelir.
        await ChangePlanAsync(staff.TenantId, SubscriptionPlanCode.Pro);
        (await SendAsync(HttpMethod.Get, $"/api/portal/v1/links/{session.LinkId}/pets", session.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await ChangePlanAsync(staff.TenantId, SubscriptionPlanCode.Basic);

        // Yeni kullanım kapalı: yeni davet reddedilir, bekleyen davet yeni bağ açamaz.
        var newClient = await SeedClientAsync(staff.TenantId);
        await ShouldBeNotInPlanAsync(await SendAsync(HttpMethod.Post, $"/api/v1/clients/{newClient.Id}/portal-invites", staff.Token), "Pro");
        (await SendAsync(HttpMethod.Get, $"/api/portal/v1/invites/{pendingToken}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Post, $"/api/portal/v1/invites/{pendingToken}/accept", body: new { acceptedLegalDocuments = PortalDocuments() }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Klinik mevcut erişimi yönetebilir.
        (await SendAsync(HttpMethod.Post, $"/api/v1/clients/{session.ClientId}/portal-links/{session.LinkId}/close", staff.Token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
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
    public async Task Portal_Settings_Should_Stay_Writable_On_Basic_For_Existing_Links()
    {
        var staff = await SeedPlanTenantAsync(SubscriptionPlanCode.Basic);

        (await SendAsync(HttpMethod.Put, $"/api/v1/clinics/{staff.ClinicId}/portal-settings", staff.Token, new { isEnabled = false }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AccessState_Should_Report_Plan_Features()
    {
        var staff = await SeedPlanTenantAsync(SubscriptionPlanCode.Pro);

        var response = await SendAsync(HttpMethod.Get, $"/api/v1/tenants/{staff.TenantId}/access-state", staff.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("planCode").GetString().Should().Be("Pro");
        var features = json.GetProperty("planFeatures").EnumerateArray()
            .ToDictionary(f => f.GetProperty("feature").GetString()!, f => f);
        features["OwnerPortal"].GetProperty("isEnabled").GetBoolean().Should().BeTrue();
        features["PortalAppointmentRequests"].GetProperty("isEnabled").GetBoolean().Should().BeTrue();
        features["MultiClinic"].GetProperty("isEnabled").GetBoolean().Should().BeFalse();
        features["MultiClinic"].GetProperty("requiredPlanCode").GetString().Should().Be("Premium");
    }
}
