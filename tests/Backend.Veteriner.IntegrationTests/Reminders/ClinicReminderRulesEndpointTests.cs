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
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Veteriner.IntegrationTests.Reminders;

/// <summary>TIER-003 ayar uçları (docs/AUTOMATED_REMINDERS_API_CONTRACT.md §3): varsayılan kapalı, paket kilidi, gün sınırı, kiracı izolasyonu.</summary>
public sealed class ClinicReminderRulesEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ClinicReminderRulesEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<(string Token, Guid ClinicId)> SeedAsync(SubscriptionPlanCode plan, IReadOnlyCollection<string>? permissions = null)
    {
        var email = $"rules-{Guid.NewGuid():N}@example.com";
        Guid clinicId;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var now = DateTime.UtcNow;
            var tenant = new Tenant($"Kural-{Guid.NewGuid():N}"[..20], now);
            var clinic = new Clinic(tenant.Id, "Merkez", "İzmir");
            var subscription = TenantSubscription.StartTrial(tenant.Id, plan, now, 14);
            subscription.ActivatePaidPlan(plan, now);
            var user = new User(email, hasher.Hash("Abc12345!"), now);
            var adminClaim = await db.OperationClaims.SingleAsync(c => c.Name == "Admin");
            db.AddRange(tenant, clinic, subscription, user, new UserTenant(user.Id, tenant.Id, now), new UserOperationClaim(user.Id, adminClaim.Id));
            await db.SaveChangesAsync();
            clinicId = clinic.Id;
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(
            _factory.Services,
            email,
            permissions ?? [PermissionCatalog.Reminders.Read, PermissionCatalog.Reminders.Manage],
            clinicId);
        return (token, clinicId);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private static string Url(Guid clinicId) => $"/api/v1/reminders/clinics/{clinicId}/rules";

    private static Dictionary<string, JsonElement> RulesOf(JsonElement json)
        => json.GetProperty("rules").EnumerateArray().ToDictionary(r => r.GetProperty("ruleType").GetString()!, r => r);

    [Fact]
    public async Task Get_Should_Return_All_Rules_Off_With_Defaults_On_Any_Plan()
    {
        var (token, clinicId) = await SeedAsync(SubscriptionPlanCode.Basic);

        var response = await SendAsync(HttpMethod.Get, Url(clinicId), token);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var rules = RulesOf(await response.Content.ReadFromJsonAsync<JsonElement>());
        rules.Keys.Should().BeEquivalentTo(["FollowUp", "Debt", "NoReturn"]);
        rules.Values.Should().OnlyContain(r => !r.GetProperty("isEnabled").GetBoolean());
        rules["FollowUp"].GetProperty("days").GetInt32().Should().Be(3);
        rules["Debt"].GetProperty("days").GetInt32().Should().Be(30);
        rules["NoReturn"].GetProperty("days").GetInt32().Should().Be(180);
        rules["FollowUp"].GetProperty("sendingAvailable").GetBoolean().Should().BeTrue();
        rules["NoReturn"].GetProperty("sendingAvailable").GetBoolean().Should().BeFalse("ticari ileti gönderimi merkezi olarak kapalı");
    }

    [Fact]
    public async Task Put_Should_Reject_Enabling_On_Basic_With_RequiredPlan()
    {
        var (token, clinicId) = await SeedAsync(SubscriptionPlanCode.Basic);

        var response = await SendAsync(HttpMethod.Put, Url(clinicId), token, new { rules = new[] { new { ruleType = "Debt", isEnabled = true, days = 30 } } });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("Subscriptions.FeatureNotInPlan");
        json.GetProperty("requiredPlanCode").GetString().Should().Be("Pro");
        json.GetProperty("feature").GetString().Should().Be("AutomatedReminders");
    }

    [Fact]
    public async Task Put_Should_Allow_Disabling_On_Basic()
    {
        var (token, clinicId) = await SeedAsync(SubscriptionPlanCode.Basic);

        (await SendAsync(HttpMethod.Put, Url(clinicId), token, new { rules = new[] { new { ruleType = "Debt", isEnabled = false, days = 45 } } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(SubscriptionPlanCode.Pro)]
    [InlineData(SubscriptionPlanCode.Premium)]
    public async Task Put_Should_Save_And_Leave_Other_Rules_Unchanged(SubscriptionPlanCode plan)
    {
        var (token, clinicId) = await SeedAsync(plan);

        var response = await SendAsync(HttpMethod.Put, Url(clinicId), token, new { rules = new[] { new { ruleType = "followup", isEnabled = true, days = 7 } } });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var rules = RulesOf(await response.Content.ReadFromJsonAsync<JsonElement>());
        rules["FollowUp"].GetProperty("isEnabled").GetBoolean().Should().BeTrue();
        rules["FollowUp"].GetProperty("days").GetInt32().Should().Be(7);
        rules["Debt"].GetProperty("isEnabled").GetBoolean().Should().BeFalse();

        var read = RulesOf(await (await SendAsync(HttpMethod.Get, Url(clinicId), token)).Content.ReadFromJsonAsync<JsonElement>());
        read["FollowUp"].GetProperty("days").GetInt32().Should().Be(7);
    }

    [Fact]
    public async Task Put_Should_Reject_Days_Out_Of_Range_Unknown_Rule_And_Duplicates()
    {
        var (token, clinicId) = await SeedAsync(SubscriptionPlanCode.Pro);

        var range = await SendAsync(HttpMethod.Put, Url(clinicId), token, new { rules = new[] { new { ruleType = "FollowUp", isEnabled = true, days = 31 } } });
        range.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(range)).Should().Be("Reminders.Rules.Validation.Days");

        (await SendAsync(HttpMethod.Put, Url(clinicId), token, new { rules = new[] { new { ruleType = "Yok", isEnabled = true, days = 3 } } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Put, Url(clinicId), token, new { rules = new[] { new { ruleType = "Debt", isEnabled = true, days = 3 }, new { ruleType = "debt", isEnabled = false, days = 3 } } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Other_Tenant_Clinic_Should_Be_NotFound_And_Permission_Required()
    {
        var (token, _) = await SeedAsync(SubscriptionPlanCode.Pro);
        var (_, foreignClinicId) = await SeedAsync(SubscriptionPlanCode.Pro);
        (await SendAsync(HttpMethod.Get, Url(foreignClinicId), token)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var (readOnlyToken, clinicId) = await SeedAsync(SubscriptionPlanCode.Pro, [PermissionCatalog.Reminders.Read]);
        (await SendAsync(HttpMethod.Put, Url(clinicId), readOnlyToken, new { rules = new[] { new { ruleType = "Debt", isEnabled = true, days = 30 } } }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
