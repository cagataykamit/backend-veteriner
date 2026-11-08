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
/// Size özel geliştirme talepleri (TIER-010; docs/CUSTOM_DEVELOPMENT_REQUESTS_API_CONTRACT.md): kiracılar arası görünmezlik, açık talep
/// sınırı, alt pakette 403 ve listenin açık kalması, platform yöneticisi dışında yönetim uçlarının reddi, durum akışı, kliniğe görünen
/// gerekçe ve maskeli denetim kaydı.
/// </summary>
public sealed class CustomDevelopmentRequestIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Url = "/api/v1/custom-development-requests";
    private const string AdminUrl = "/api/v1/admin/custom-development-requests";
    private const string SecretText = "GizliMetinPamukKedi";

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CustomDevelopmentRequestIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ---------- yardımcılar ----------

    private sealed record Ctx(string Token, Guid TenantId, string TenantName);

    private async Task<Ctx> SeedAsync(SubscriptionPlanCode plan, IReadOnlyCollection<string>? permissions = null)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var tenant = new Tenant($"Ozel-{Guid.NewGuid():N}"[..16], DateTime.UtcNow);
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

    private static object Body(string title = "Gün sonu raporuna sütun")
        => new
        {
            requestTitle = title,
            requestText = $"Hekim adı sütunu eklensin. {SecretText}",
            targetKind = "Report",
            requestTarget = "Gün sonu raporu",
            priority = "High",
        };

    private Task<HttpResponseMessage> CreateAsync(Ctx ctx, string title = "Gün sonu raporuna sütun")
        => SendAsync(HttpMethod.Post, Url, ctx.Token, Body(title));

    private async Task<Guid> CreateOkAsync(Ctx ctx, string title = "Gün sonu raporuna sütun")
        => (await OkJsonAsync(await CreateAsync(ctx, title))).GetProperty("id").GetGuid();

    private async Task<JsonElement> ListAsync(Ctx ctx) => await OkJsonAsync(await SendAsync(HttpMethod.Get, Url, ctx.Token));

    private Task<HttpResponseMessage> ChangeStatusAsync(string admin, Guid id, string status, string? reason = null, string? note = null)
        => SendAsync(HttpMethod.Post, $"{AdminUrl}/{id}/status", admin, new { status, decisionReason = reason, responseNote = note });

    private static IEnumerable<Guid> Ids(JsonElement list) => list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid());

    // ---------- kiracı ----------

    [Fact]
    public async Task Tenant_Should_Only_See_Own_Requests()
    {
        var a = await SeedAsync(SubscriptionPlanCode.Premium);
        var b = await SeedAsync(SubscriptionPlanCode.Premium);

        var created = await OkJsonAsync(await CreateAsync(a));
        created.GetProperty("status").GetString().Should().Be("Received");
        created.GetProperty("targetKind").GetString().Should().Be("Report");
        created.GetProperty("priority").GetString().Should().Be("High");
        created.GetProperty("isOpen").GetBoolean().Should().BeTrue();
        var id = created.GetProperty("id").GetGuid();

        var listA = await ListAsync(a);
        Ids(listA).Should().Equal(id);
        listA.GetProperty("openCount").GetInt32().Should().Be(1);
        listA.GetProperty("maxOpen").GetInt32().Should().Be(CustomDevelopmentRequest.MaxOpenPerTenant);

        var listB = await ListAsync(b);
        Ids(listB).Should().BeEmpty("başka kiracının talebi görünmez");
        listB.GetRawText().Should().NotContain(SecretText);
    }

    [Fact]
    public async Task Open_Limit_Should_Return_TooManyOpen_Until_A_Request_Is_Closed()
    {
        var ctx = await SeedAsync(SubscriptionPlanCode.Premium);
        var ids = new List<Guid>();
        for (var i = 0; i < CustomDevelopmentRequest.MaxOpenPerTenant; i++)
            ids.Add(await CreateOkAsync(ctx, $"Talep {i}"));

        await ShouldFailAsync(await CreateAsync(ctx, "Fazla talep"), HttpStatusCode.Conflict, "CustomDevelopment.TooManyOpen");
        (await ListAsync(ctx)).GetProperty("items").GetArrayLength().Should().Be(CustomDevelopmentRequest.MaxOpenPerTenant);

        // Planlanan talep hâlâ açık sayılır; kapsam dışı kararı yer açar.
        var admin = await PlatformAdminTokenAsync();
        await OkJsonAsync(await ChangeStatusAsync(admin, ids[0], "InReview"));
        await OkJsonAsync(await ChangeStatusAsync(admin, ids[0], "Planned"));
        await ShouldFailAsync(await CreateAsync(ctx, "Hâlâ fazla"), HttpStatusCode.Conflict, "CustomDevelopment.TooManyOpen");

        await OkJsonAsync(await ChangeStatusAsync(admin, ids[1], "InReview"));
        await OkJsonAsync(await ChangeStatusAsync(admin, ids[1], "OutOfScope", "Vetinity dışı bir sistem"));
        await CreateOkAsync(ctx, "Yer açıldı");
        (await ListAsync(ctx)).GetProperty("openCount").GetInt32().Should().Be(CustomDevelopmentRequest.MaxOpenPerTenant);
    }

    [Theory]
    [InlineData(SubscriptionPlanCode.Basic)]
    [InlineData(SubscriptionPlanCode.Pro)]
    public async Task Lower_Plan_Should_Get_403_On_Create_But_List_Stays_Open(SubscriptionPlanCode plan)
    {
        var ctx = await SeedAsync(plan);

        var response = await CreateAsync(ctx);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("Subscriptions.FeatureNotInPlan");
        problem.GetProperty("requiredPlanCode").GetString().Should().Be("Premium");

        Ids(await ListAsync(ctx)).Should().BeEmpty();
    }

    [Fact]
    public async Task Downgraded_Tenant_Should_Keep_Seeing_Requests()
    {
        var ctx = await SeedAsync(SubscriptionPlanCode.Premium);
        var id = await CreateOkAsync(ctx);
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var subscription = await db.TenantSubscriptions.SingleAsync(s => s.TenantId == ctx.TenantId);
            subscription.ActivatePaidPlan(SubscriptionPlanCode.Basic, DateTime.UtcNow);
            await db.SaveChangesAsync();
        }

        Ids(await ListAsync(ctx)).Should().Equal(id);
        await ShouldFailAsync(await CreateAsync(ctx), HttpStatusCode.Forbidden, "Subscriptions.FeatureNotInPlan");
    }

    [Fact]
    public async Task Tenant_Endpoints_Should_Require_Permission_And_Validate()
    {
        var withoutPermission = await SeedAsync(SubscriptionPlanCode.Premium, [PermissionCatalog.Clinics.Read]);
        (await SendAsync(HttpMethod.Get, Url, withoutPermission.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CreateAsync(withoutPermission)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, Url, null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var ctx = await SeedAsync(SubscriptionPlanCode.Premium);
        await ShouldFailAsync(
            await SendAsync(HttpMethod.Post, Url, ctx.Token, new { requestTitle = " ", requestText = "x", targetKind = "Screen", requestTarget = "y", priority = "Low" }),
            HttpStatusCode.BadRequest, CustomDevelopmentRequest.ValidationCode);
        (await SendAsync(HttpMethod.Post, Url, ctx.Token, new { requestTitle = "b", requestText = "x", requestTarget = "y", priority = "Low" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "konu seçimi zorunlu");
    }

    // ---------- platform yöneticisi ----------

    [Fact]
    public async Task Admin_Endpoints_Should_Reject_Everyone_But_Platform_Admin()
    {
        var ctx = await SeedAsync(SubscriptionPlanCode.Premium,
            [PermissionCatalog.CustomDevelopmentRequests.Manage, PermissionCatalog.Clinics.Read, PermissionCatalog.Clinics.Update]);
        var id = await CreateOkAsync(ctx);

        (await SendAsync(HttpMethod.Get, AdminUrl, null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SendAsync(HttpMethod.Get, AdminUrl, ctx.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "kiracı yöneticisi tüm talepleri göremez");
        (await ChangeStatusAsync(ctx.Token, id, "InReview")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "kiracı kendi talebinin durumunu değiştiremez");

        (await ListAsync(ctx)).GetProperty("items")[0].GetProperty("status").GetString().Should().Be("Received");
    }

    [Fact]
    public async Task Admin_Should_List_All_Tenants_Move_Status_And_Audit_Masked()
    {
        var a = await SeedAsync(SubscriptionPlanCode.Premium);
        var b = await SeedAsync(SubscriptionPlanCode.Premium);
        var idA = await CreateOkAsync(a, "A talebi");
        var idB = await CreateOkAsync(b, "B talebi");
        var admin = await PlatformAdminTokenAsync();

        var page = await OkJsonAsync(await SendAsync(HttpMethod.Get, $"{AdminUrl}?openOnly=true&pageSize=200", admin));
        var rows = page.GetProperty("items").EnumerateArray().ToList();
        var rowA = rows.Single(r => r.GetProperty("request").GetProperty("id").GetGuid() == idA);
        rowA.GetProperty("tenantId").GetGuid().Should().Be(a.TenantId);
        rowA.GetProperty("tenantName").GetString().Should().Be(a.TenantName);
        rows.Should().Contain(r => r.GetProperty("request").GetProperty("id").GetGuid() == idB);

        // Akış dışı geçiş ve gerekçesiz kapsam dışı reddedilir.
        await ShouldFailAsync(await ChangeStatusAsync(admin, idA, "Completed"), HttpStatusCode.Conflict, "CustomDevelopment.InvalidStatusTransition");
        await OkJsonAsync(await ChangeStatusAsync(admin, idA, "InReview", note: "İnceliyoruz"));
        await ShouldFailAsync(await ChangeStatusAsync(admin, idA, "OutOfScope"), HttpStatusCode.BadRequest, CustomDevelopmentRequest.ValidationCode);
        var changed = await OkJsonAsync(await ChangeStatusAsync(admin, idA, "OutOfScope", $"Muhasebe programı işi {SecretText}"));
        changed.GetProperty("request").GetProperty("status").GetString().Should().Be("OutOfScope");
        await ShouldFailAsync(await ChangeStatusAsync(admin, Guid.NewGuid(), "InReview"), HttpStatusCode.NotFound, "CustomDevelopment.NotFound");

        var byStatus = await OkJsonAsync(await SendAsync(HttpMethod.Get, $"{AdminUrl}?status=OutOfScope&pageSize=200", admin));
        byStatus.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("request").GetProperty("id").GetGuid()).Should().Contain(idA).And.NotContain(idB);

        // Klinik gerekçeyi ve notu görür; diğer kiracı görmez.
        var seen = (await ListAsync(a)).GetProperty("items")[0];
        seen.GetProperty("status").GetString().Should().Be("OutOfScope");
        seen.GetProperty("decisionReason").GetString().Should().StartWith("Muhasebe programı işi");
        seen.GetProperty("responseNote").GetString().Should().Be("İnceliyoruz");
        seen.GetProperty("isOpen").GetBoolean().Should().BeFalse();
        (await ListAsync(b)).GetRawText().Should().NotContain("Muhasebe");

        // Planlandı → Tamamlandı.
        await OkJsonAsync(await ChangeStatusAsync(admin, idB, "InReview"));
        await OkJsonAsync(await ChangeStatusAsync(admin, idB, "Planned", note: "Aralık sürümünde"));
        (await OkJsonAsync(await ChangeStatusAsync(admin, idB, "Completed"))).GetProperty("request").GetProperty("status").GetString().Should().Be("Completed");

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logs = await db.AuditLogs.AsNoTracking()
            .Where(l => l.Action == "CustomDevelopmentRequest.StatusChange" && l.TargetId!.Contains(idA.ToString()))
            .ToListAsync();
        logs.Should().Contain(l => l.Success && l.TargetId == $"CustomDevelopmentRequestId={idA};Status=OutOfScope");
        logs.Should().Contain(l => !l.Success, "reddedilen değişiklik de yazılır");
        logs.Select(l => l.RequestPayload ?? "").Should().AllSatisfy(p => p.Should().NotContain(SecretText).And.NotContain("İnceliyoruz"));

        var createLogs = await db.AuditLogs.AsNoTracking().Where(l => l.Action == "CustomDevelopmentRequest.Create" && l.Success).ToListAsync();
        createLogs.Should().NotBeEmpty();
        createLogs.Select(l => l.RequestPayload ?? "").Should().AllSatisfy(p => p.Should().NotContain(SecretText).And.NotContain("A talebi"));
    }
}
