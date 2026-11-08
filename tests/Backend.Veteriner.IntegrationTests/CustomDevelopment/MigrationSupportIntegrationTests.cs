using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Domain.CustomDevelopment;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.CustomDevelopment;

/// <summary>
/// Geçiş desteği ve kurulum görüşmesi talebi (TIER-021; docs/CUSTOM_DEVELOPMENT_REQUESTS_API_CONTRACT.md §11): her pakette açılır,
/// ücret bilgisi pakete göre, kiracılar arası görünmez, özel geliştirme listesine karışmaz, platform talepler listesinde görünür ve aynı
/// durum akışını izler; not denetim kaydında maskelenir.
/// </summary>
public sealed class MigrationSupportIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Url = "/api/v1/custom-development-requests/migration-support";
    private const string CustomDevelopmentUrl = "/api/v1/custom-development-requests";
    private const string AdminUrl = "/api/v1/admin/custom-development-requests";
    private const string SecretText = "GizliNotPamukKedi";

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public MigrationSupportIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private sealed record Ctx(string Token, Guid TenantId, string TenantName);

    private async Task<Ctx> SeedAsync(SubscriptionPlanCode plan, IReadOnlyCollection<string>? permissions = null)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var tenant = new Tenant($"Gecis-{Guid.NewGuid():N}"[..16], DateTime.UtcNow);
        db.Tenants.Add(tenant);
        var subscription = TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14);
        subscription.ActivatePaidPlan(plan, DateTime.UtcNow);
        db.TenantSubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        var token = await IntegrationTestAuthHelper.SeedTenantWideAdminAndIssueTokenAsync(
            db, jwt, hasher, tenant.Id, permissions ?? [PermissionCatalog.CustomDevelopmentRequests.Manage]);
        return new Ctx(token, tenant.Id, tenant.Name);
    }

    private async Task<string> PlatformAdminTokenAsync()
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _, _) = jwt.Create(
            Guid.NewGuid(), $"destek-{Guid.NewGuid():N}@example.com", Array.Empty<string>(),
            [new Claim(VeterinerClaims.PlatformAdmin, bool.TrueString)]);
        return token;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? bearer, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (bearer is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> OkJsonAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task ShouldFailAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.Should().Be(status, await response.Content.ReadAsStringAsync());
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be(code);
    }

    private static object Body(string sourceProgram = "Kolayvet", string meetingDays = "Monday, Thursday")
        => new
        {
            sourceProgram,
            otherProgramName = (string?)null,
            recordVolume = "UpTo5000",
            meetingDays,
            meetingTimes = "Morning, Afternoon",
            note = $"Üç yıllık kayıt var. {SecretText}",
        };

    private async Task<JsonElement> CreateOkAsync(Ctx ctx, object? body = null)
        => await OkJsonAsync(await SendAsync(HttpMethod.Post, Url, ctx.Token, body ?? Body()));

    [Theory]
    [InlineData(SubscriptionPlanCode.Basic, "Quoted")]
    [InlineData(SubscriptionPlanCode.Pro, "Quoted")]
    [InlineData(SubscriptionPlanCode.Premium, "Free")]
    public async Task Every_Plan_Should_Open_Request_With_Plan_Fee(SubscriptionPlanCode plan, string fee)
    {
        var ctx = await SeedAsync(plan);

        var overview = await OkJsonAsync(await SendAsync(HttpMethod.Get, Url, ctx.Token));
        overview.GetProperty("fee").GetString().Should().Be(fee);
        overview.GetProperty("meetingDurationMinutes").GetInt32().Should().Be(60);
        overview.GetProperty("items").GetArrayLength().Should().Be(0);

        var created = await CreateOkAsync(ctx);
        created.GetProperty("targetKind").GetString().Should().Be("Migration");
        created.GetProperty("requestTarget").GetString().Should().Be("Kolayvet");
        created.GetProperty("status").GetString().Should().Be("Received");
        var migration = created.GetProperty("migration");
        migration.GetProperty("sourceProgram").GetString().Should().Be("Kolayvet");
        migration.GetProperty("recordVolume").GetString().Should().Be("UpTo5000");
        migration.GetProperty("meetingDays").GetString().Should().Be("Monday, Thursday");
        migration.GetProperty("meetingTimes").GetString().Should().Be("Morning, Afternoon");
        migration.GetProperty("fee").GetString().Should().Be(fee);
        migration.GetProperty("meetingDurationMinutes").GetInt32().Should().Be(60);

        var after = await OkJsonAsync(await SendAsync(HttpMethod.Get, Url, ctx.Token));
        after.GetProperty("openCount").GetInt32().Should().Be(1);
        after.GetProperty("items")[0].GetProperty("id").GetGuid().Should().Be(created.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Requests_Should_Be_Isolated_And_Not_Mix_With_Custom_Development()
    {
        var a = await SeedAsync(SubscriptionPlanCode.Premium);
        var b = await SeedAsync(SubscriptionPlanCode.Premium);
        var idA = (await CreateOkAsync(a)).GetProperty("id").GetGuid();

        var listB = await OkJsonAsync(await SendAsync(HttpMethod.Get, Url, b.Token));
        listB.GetProperty("items").GetArrayLength().Should().Be(0);
        listB.GetRawText().Should().NotContain(SecretText);

        // Geçiş talebi özel geliştirme listesinde ve 3'lük sınırında yoktur.
        var customA = await OkJsonAsync(await SendAsync(HttpMethod.Get, CustomDevelopmentUrl, a.Token));
        customA.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().NotContain(idA);
        customA.GetProperty("openCount").GetInt32().Should().Be(0);
        await ShouldFailAsync(
            await SendAsync(HttpMethod.Post, CustomDevelopmentUrl, a.Token,
                new { requestTitle = "x", requestText = "y", targetKind = "Migration", requestTarget = "z", priority = "Normal" }),
            HttpStatusCode.BadRequest, "Validation.FluentValidation");
    }

    [Fact]
    public async Task Second_Open_Request_Should_Conflict_Until_First_Is_Closed()
    {
        var ctx = await SeedAsync(SubscriptionPlanCode.Basic);
        var id = (await CreateOkAsync(ctx)).GetProperty("id").GetGuid();

        await ShouldFailAsync(await SendAsync(HttpMethod.Post, Url, ctx.Token, Body("EVet")), HttpStatusCode.Conflict,
            "CustomDevelopment.MigrationAlreadyOpen");

        var admin = await PlatformAdminTokenAsync();
        await OkJsonAsync(await SendAsync(HttpMethod.Post, $"{AdminUrl}/{id}/status", admin, new { status = "InReview" }));
        await OkJsonAsync(await SendAsync(HttpMethod.Post, $"{AdminUrl}/{id}/status", admin, new { status = "Planned", responseNote = "Görüşme Salı 10.00" }));
        await OkJsonAsync(await SendAsync(HttpMethod.Post, $"{AdminUrl}/{id}/status", admin, new { status = "Completed" }));

        (await CreateOkAsync(ctx, Body("EVet"))).GetProperty("requestTarget").GetString().Should().Be("E-vet");
        var overview = await OkJsonAsync(await SendAsync(HttpMethod.Get, Url, ctx.Token));
        overview.GetProperty("items").GetArrayLength().Should().Be(2);
        overview.GetProperty("items")[1].GetProperty("responseNote").GetString().Should().Be("Görüşme Salı 10.00");
    }

    [Fact]
    public async Task Platform_Admin_Should_See_Request_In_Existing_List_With_Kind_Filter_And_Audit_Masked()
    {
        var ctx = await SeedAsync(SubscriptionPlanCode.Pro);
        var id = (await CreateOkAsync(ctx)).GetProperty("id").GetGuid();
        var admin = await PlatformAdminTokenAsync();

        var page = await OkJsonAsync(await SendAsync(HttpMethod.Get, $"{AdminUrl}?targetKind=Migration&openOnly=true&pageSize=200", admin));
        var rows = page.GetProperty("items").EnumerateArray().ToList();
        rows.Should().OnlyContain(r => r.GetProperty("request").GetProperty("targetKind").GetString() == "Migration");
        var row = rows.Single(r => r.GetProperty("request").GetProperty("id").GetGuid() == id);
        row.GetProperty("tenantName").GetString().Should().Be(ctx.TenantName);
        row.GetProperty("nextStatuses").EnumerateArray().Select(s => s.GetString()).Should().Equal("InReview");
        row.GetProperty("request").GetProperty("migration").GetProperty("fee").GetString().Should().Be("Quoted");

        var all = await OkJsonAsync(await SendAsync(HttpMethod.Get, $"{AdminUrl}?pageSize=200", admin));
        all.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("request").GetProperty("id").GetGuid()).Should().Contain(id);
        var reportOnly = await OkJsonAsync(await SendAsync(HttpMethod.Get, $"{AdminUrl}?targetKind=Report&pageSize=200", admin));
        reportOnly.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("request").GetProperty("id").GetGuid()).Should().NotContain(id);

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logs = await db.AuditLogs.AsNoTracking()
            .Where(l => l.Action == "CustomDevelopmentRequest.CreateMigrationSupport")
            .ToListAsync();
        logs.Should().Contain(l => l.Success && l.TargetId == "SourceProgram=Kolayvet;RecordVolume=UpTo5000");
        logs.Select(l => l.RequestPayload ?? "").Should().AllSatisfy(p => p.Should().NotContain(SecretText));
    }

    [Fact]
    public async Task Endpoints_Should_Require_Permission_And_Validate()
    {
        var noPermission = await SeedAsync(SubscriptionPlanCode.Premium, ["Clients.Read"]);
        (await SendAsync(HttpMethod.Get, Url, noPermission.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, Url, noPermission.Token, Body())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, Url, null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var ctx = await SeedAsync(SubscriptionPlanCode.Basic);
        await ShouldFailAsync(await SendAsync(HttpMethod.Post, Url, ctx.Token, Body(meetingDays: "None")), HttpStatusCode.BadRequest,
            CustomDevelopmentRequest.ValidationCode);
        (await SendAsync(HttpMethod.Post, Url, ctx.Token, new { sourceProgram = "Kolayvet" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
