using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Ardalis.Specification.EntityFrameworkCore;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Api.Configuration;
using Backend.Veteriner.Api.Controllers;
using Backend.Veteriner.Api.Middleware;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.ClinicSites;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.ClinicSites;
using Backend.Veteriner.Domain.Files;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.ClinicSites;

/// <summary>
/// Klinik web sitesi (ADR-045): kiracılar arası erişim reddi, taslağın herkese açık uçtan görünmemesi, yayın zorunlu listesi,
/// eski adres çözümü ve yeniden kullanılamaması, sürümün değişmezliği, yanıtta kiracı kimliği olmaması, güvenli olmayan bağlantı
/// reddi, görsel türü denetimi, abonelikten doğan kapanma ve hız sınırı politikası.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class ClinicSiteIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] AdminPermissions = [PermissionCatalog.Clinics.Read, PermissionCatalog.Clinics.Update];

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, .. "JFIF"u8.ToArray(), 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52];
    private static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj << >> endobj\ntrailer << >>\n%%EOF"u8.ToArray();

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ClinicSiteIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ---------- yardımcılar ----------

    private sealed record Ctx(string Token, Guid TenantId, Guid ClinicId, string Url);

    private async Task<Ctx> SeedAsync(bool complete = true, IReadOnlyCollection<string>? permissions = null)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = DateTime.UtcNow;

        var tenant = new Tenant($"Site-{Guid.NewGuid():N}"[..16], now);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, now, trialDays: 14));

        var clinic = complete
            ? new Clinic(tenant.Id, $"Çağrı Veteriner {Guid.NewGuid():N}"[..28], "İstanbul", "05550000000", "klinik@example.com", "Moda Cad. 1", "tanıtım", "Kadıköy")
            : new Clinic(tenant.Id, $"Eksik Klinik {Guid.NewGuid():N}"[..24], "İstanbul");
        db.Clinics.Add(clinic);
        if (complete)
        {
            db.ClinicWorkingHours.Add(ClinicWorkingHour.Create(
                tenant.Id, clinic.Id, DayOfWeek.Monday, false, new TimeOnly(9, 0), new TimeOnly(18, 0), new TimeOnly(12, 0), new TimeOnly(13, 0), now));
        }

        await db.SaveChangesAsync();

        var token = await IntegrationTestAuthHelper.SeedTenantWideAdminAndIssueTokenAsync(db, jwt, hasher, tenant.Id, permissions ?? AdminPermissions);
        return new Ctx(token, tenant.Id, clinic.Id, $"/api/v1/clinics/{clinic.Id}/site");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? bearer = null, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (bearer is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> PublicAsync(string slug) => _client.GetAsync($"/api/v1/public/sites/{slug}");

    private static async Task<JsonElement> OkJsonAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<JsonElement> PublicJsonAsync(string slug) => await OkJsonAsync(await PublicAsync(slug));

    private static async Task ShouldFailAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.Should().Be(status, await response.Content.ReadAsStringAsync());
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be(code);
    }

    private static object Config(string marker = "ilk metin", string? imageId = null)
        => new
        {
            schemaVersion = 1,
            content = new
            {
                clinic = new { slogan = marker, website = "https://ornek.example.com" },
                heroImage = imageId is null ? null : new { src = $"site-image:{imageId}", alt = "Kedi" },
            },
            design = new { conceptKey = "modern", sections = new { hero = "alt-1" } },
        };

    private async Task<JsonElement> SaveDraftAsync(Ctx ctx, object config, string? rowVersion = null)
        => await OkJsonAsync(await SendAsync(HttpMethod.Put, $"{ctx.Url}/draft", ctx.Token, new { config, rowVersion }));

    private async Task<string> RowVersionAsync(Ctx ctx)
        => (await OkJsonAsync(await SendAsync(HttpMethod.Get, ctx.Url, ctx.Token))).GetProperty("site").GetProperty("rowVersion").GetString()!;

    private async Task<JsonElement> PublishAsync(Ctx ctx)
        => await OkJsonAsync(await SendAsync(HttpMethod.Post, $"{ctx.Url}/publish", ctx.Token, new { rowVersion = await RowVersionAsync(ctx) }));

    /// <summary>Taslağı kaydedip yayınlar, güncel adresi döner.</summary>
    private async Task<string> PublishNewSiteAsync(Ctx ctx, object? config = null)
    {
        await SaveDraftAsync(ctx, config ?? Config());
        return (await PublishAsync(ctx)).GetProperty("slug").GetString()!;
    }

    private async Task<JsonElement> UpdateDraftAsync(Ctx ctx, object config)
        => await SaveDraftAsync(ctx, config, await RowVersionAsync(ctx));

    private async Task<HttpResponseMessage> UploadAsync(Ctx ctx, byte[] content, string declaredType = "image/jpeg", string fileName = "a.jpg")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(declaredType);
        form.Add(file, "file", fileName);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ctx.Url}/images") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);
        return await _client.SendAsync(request);
    }

    private async Task<string> UploadOkAsync(Ctx ctx, byte[]? content = null)
    {
        var response = await UploadAsync(ctx, content ?? Jpeg);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = json.GetProperty("id").GetString()!;
        json.GetProperty("reference").GetString().Should().Be($"site-image:{id}");
        return id;
    }

    private async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task SetSubscriptionStatusAsync(Guid tenantId, TenantSubscriptionStatus status)
        => WithDbAsync(db => db.TenantSubscriptions.Where(s => s.TenantId == tenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status)));

    private Task ExpireTrialAsync(Guid tenantId)
        => WithDbAsync(db => db.TenantSubscriptions.Where(s => s.TenantId == tenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.TrialEndsAtUtc, DateTime.UtcNow.AddMinutes(-1))));

    private Task PayAsync(Guid tenantId)
        => WithDbAsync(async db =>
        {
            var subscription = await db.TenantSubscriptions.SingleAsync(s => s.TenantId == tenantId);
            subscription.ActivatePaidPlan(SubscriptionPlanCode.Basic, DateTime.UtcNow);
            await db.SaveChangesAsync();
        });

    // ---------- taslak ve yayın ----------

    [Fact]
    public async Task Get_Should_Suggest_Address_When_No_Site()
    {
        var ctx = await SeedAsync();

        var json = await OkJsonAsync(await SendAsync(HttpMethod.Get, ctx.Url, ctx.Token));

        json.GetProperty("exists").GetBoolean().Should().BeFalse();
        json.GetProperty("suggestedSlug").GetString().Should().StartWith("cagri-veteriner-");
        json.GetProperty("site").ValueKind.Should().Be(JsonValueKind.Null);
        json.GetProperty("missingForPublish").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Draft_Should_Create_Site_And_Round_Trip_Unknown_Design_Fields()
    {
        var ctx = await SeedAsync();

        var state = await SaveDraftAsync(ctx, new { schemaVersion = 1, design = new { yeniPaketAlani = new { derin = new[] { 1, 2 } } } });

        state.GetProperty("slug").GetString().Should().StartWith("cagri-veteriner-");
        state.GetProperty("publicPath").GetString().Should().Be($"/k/{state.GetProperty("slug").GetString()}");
        state.GetProperty("publishedVersionNumber").ValueKind.Should().Be(JsonValueKind.Null);
        state.GetProperty("hasUnpublishedChanges").GetBoolean().Should().BeTrue();

        var detail = await OkJsonAsync(await SendAsync(HttpMethod.Get, ctx.Url, ctx.Token));
        detail.GetProperty("exists").GetBoolean().Should().BeTrue();
        detail.GetProperty("draft").GetProperty("design").GetProperty("yeniPaketAlani").GetProperty("derin")[1].GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Draft_Should_Require_Fresh_RowVersion_Once_Site_Exists()
    {
        var ctx = await SeedAsync();
        var stale = (await SaveDraftAsync(ctx, Config("bir"))).GetProperty("rowVersion").GetString();
        await SaveDraftAsync(ctx, Config("iki"), stale);

        await ShouldFailAsync(
            await SendAsync(HttpMethod.Put, $"{ctx.Url}/draft", ctx.Token, new { config = Config("uc"), rowVersion = stale }),
            HttpStatusCode.Conflict,
            "ClinicSites.ConcurrencyConflict");
        await ShouldFailAsync(
            await SendAsync(HttpMethod.Put, $"{ctx.Url}/draft", ctx.Token, new { config = Config("uc") }),
            HttpStatusCode.BadRequest,
            "ClinicSites.Validation");
    }

    [Fact]
    public async Task Draft_Should_Not_Be_Visible_Publicly_Before_Or_After_Publish()
    {
        var ctx = await SeedAsync();
        var slug = (await SaveDraftAsync(ctx, Config("yayinlanmamis-metin"))).GetProperty("slug").GetString()!;

        (await PublicAsync(slug)).StatusCode.Should().Be(HttpStatusCode.NotFound, "hiç yayınlanmamış site yoktur");

        await PublishAsync(ctx);
        await UpdateDraftAsync(ctx, Config("taslakta-kalan-metin"));

        var response = await PublicAsync(slug);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        body.Should().Contain("yayinlanmamis-metin");
        body.Should().NotContain("taslakta-kalan-metin");
        body.Should().NotContain("draft", "yanıtta taslak alanı yoktur");

        var detail = await OkJsonAsync(await SendAsync(HttpMethod.Get, ctx.Url, ctx.Token));
        detail.GetProperty("site").GetProperty("hasUnpublishedChanges").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Public_Response_Should_Carry_Only_Allowed_Fields()
    {
        var ctx = await SeedAsync();
        var slug = await PublishNewSiteAsync(ctx);
        Guid userId = default;
        Guid siteId = default;
        await WithDbAsync(async db =>
        {
            var site = await db.ClinicSites.SingleAsync(s => s.ClinicId == ctx.ClinicId);
            siteId = site.Id;
            userId = (await db.ClinicSiteVersions.SingleAsync(v => v.SiteId == site.Id)).PublishedByUserId;
        });

        var response = await PublicAsync(slug);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body).RootElement;

        json.GetProperty("status").GetString().Should().Be("Published");
        json.GetProperty("versionNumber").GetInt32().Should().Be(1);
        json.GetProperty("config").GetProperty("content").GetProperty("clinic").GetProperty("slogan").GetString().Should().Be("ilk metin");

        json.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "status", "slug", "redirectToSlug", "message", "versionNumber", "publishedAtUtc", "config", "clinic", "onlineBooking");

        var clinic = json.GetProperty("clinic");
        clinic.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "name", "city", "district", "address", "phone", "email", "workingHours");
        clinic.GetProperty("phone").GetString().Should().Be("05550000000");
        clinic.GetProperty("district").GetString().Should().Be("Kadıköy");
        var hours = clinic.GetProperty("workingHours");
        hours.GetArrayLength().Should().Be(1);
        hours[0].GetProperty("dayOfWeek").GetInt32().Should().Be((int)DayOfWeek.Monday);
        hours[0].GetProperty("opensAt").GetString().Should().Be("09:00:00");
        response.Headers.CacheControl?.Public.Should().NotBe(true, "kapanma hemen görünsün diye JSON önbelleğe alınmaz");

        foreach (var secret in new[] { ctx.TenantId, ctx.ClinicId, siteId, userId })
            body.Should().NotContain(secret.ToString(), "kiracı, klinik, site ve kullanıcı kimliği dışarı çıkmaz");
        body.Should().NotContainEquivalentOf("tenant");
    }

    [Fact]
    public async Task Clinic_Record_Change_Should_Appear_Without_Republishing()
    {
        var ctx = await SeedAsync();
        var slug = await PublishNewSiteAsync(ctx);

        await WithDbAsync(async db =>
        {
            var clinic = await db.Clinics.SingleAsync(c => c.Id == ctx.ClinicId);
            clinic.UpdateDetails(clinic.Name, clinic.City, "05551112233", clinic.Email, clinic.Address, clinic.Description, clinic.District);
            await db.SaveChangesAsync();
        });

        (await PublicJsonAsync(slug)).GetProperty("clinic").GetProperty("phone").GetString().Should().Be("05551112233");
        (await PublicJsonAsync(slug)).GetProperty("versionNumber").GetInt32().Should().Be(1);
    }

    // ---------- yayın zorunlu listesi ----------

    [Fact]
    public async Task Publish_Should_List_Missing_Requirements()
    {
        var ctx = await SeedAsync(complete: false);
        var rowVersion = (await SaveDraftAsync(ctx, Config())).GetProperty("rowVersion").GetString();

        var response = await SendAsync(HttpMethod.Post, $"{ctx.Url}/publish", ctx.Token, new { rowVersion });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("ClinicSites.PublishRequirementsNotMet");
        problem.GetProperty("missing").EnumerateArray().Select(m => m.GetString())
            .Should().BeEquivalentTo("phone", "address", "workingHours");
        problem.GetProperty("detail").GetString().Should().Contain("telefon");

        var detail = await OkJsonAsync(await SendAsync(HttpMethod.Get, ctx.Url, ctx.Token));
        detail.GetProperty("missingForPublish").GetArrayLength().Should().Be(3);
        detail.GetProperty("site").GetProperty("publishedVersionNumber").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Publish_Should_Reject_Stale_RowVersion()
    {
        var ctx = await SeedAsync();
        var stale = (await SaveDraftAsync(ctx, Config("bir"))).GetProperty("rowVersion").GetString();
        await SaveDraftAsync(ctx, Config("iki"), stale);

        await ShouldFailAsync(
            await SendAsync(HttpMethod.Post, $"{ctx.Url}/publish", ctx.Token, new { rowVersion = stale }),
            HttpStatusCode.Conflict,
            "ClinicSites.ConcurrencyConflict");
    }

    [Fact]
    public async Task Double_Publish_Should_Never_Return_500()
    {
        var ctx = await SeedAsync();
        await SaveDraftAsync(ctx, Config());

        for (var round = 0; round < 5; round++)
        {
            var rowVersion = await RowVersionAsync(ctx);
            var responses = await Task.WhenAll(
                SendAsync(HttpMethod.Post, $"{ctx.Url}/publish", ctx.Token, new { rowVersion }),
                SendAsync(HttpMethod.Post, $"{ctx.Url}/publish", ctx.Token, new { rowVersion }));

            responses.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.OK || s == HttpStatusCode.Conflict);
            responses.Should().ContainSingle(r => r.StatusCode == HttpStatusCode.OK, "aynı sürümden yalnız bir yayın geçer");
            var conflict = responses.Single(r => r.StatusCode == HttpStatusCode.Conflict);
            (await IntegrationTestProblemDetails.ReadCodeAsync(conflict)).Should().Be("ClinicSites.ConcurrencyConflict");
        }
    }

    [Fact]
    public async Task Public_Queries_Should_Not_Select_Draft_Or_Config_Json()
    {
        await WithDbAsync(db =>
        {
            var tenantId = Guid.NewGuid();
            var siteQuery = SpecificationEvaluator.Default
                .GetQuery(db.ClinicSites, new PublicSiteRecordByIdSpec(tenantId, Guid.NewGuid()))
                .ToQueryString();
            siteQuery.Should().NotContain(nameof(ClinicSite.DraftConfigJson));

            var imageQuery = SpecificationEvaluator.Default
                .GetQuery(db.ClinicSiteVersions, new ClinicSiteVersionReferencedImagesSpec(tenantId, Guid.NewGuid(), 1))
                .ToQueryString();
            imageQuery.Should().NotContain(nameof(ClinicSiteVersion.ConfigJson));
            imageQuery.Should().Contain(nameof(ClinicSiteVersion.ReferencedImageIds));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Publish_Should_Reject_Inactive_Clinic()
    {
        var ctx = await SeedAsync();
        await SaveDraftAsync(ctx, Config());
        await WithDbAsync(db => db.Clinics.Where(c => c.Id == ctx.ClinicId).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false)));

        await ShouldFailAsync(
            await SendAsync(HttpMethod.Post, $"{ctx.Url}/publish", ctx.Token, new { rowVersion = await RowVersionAsync(ctx) }),
            HttpStatusCode.Conflict,
            "ClinicSites.ClinicInactive");
    }

    // ---------- güvenlik sınırları ----------

    [Fact]
    public async Task Draft_Should_Reject_Javascript_Link()
    {
        var ctx = await SeedAsync();

        var response = await SendAsync(HttpMethod.Put, $"{ctx.Url}/draft", ctx.Token, new
        {
            config = new { schemaVersion = 1, content = new { clinic = new { mapUrl = "JavaScript:alert(1)" } } },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("ClinicSites.Validation");
        var issue = problem.GetProperty("issues")[0];
        issue.GetProperty("path").GetString().Should().Be("$.content.clinic.mapUrl");
        issue.GetProperty("reason").GetString().Should().Be(ClinicSiteConfig.Reasons.LinkSchemeNotAllowed);
    }

    [Fact]
    public async Task Draft_Should_Reject_Clinic_Record_Fields_Missing_Version_And_Empty_Body()
    {
        var ctx = await SeedAsync();

        await ShouldFailAsync(
            await SendAsync(HttpMethod.Put, $"{ctx.Url}/draft", ctx.Token, new { config = new { schemaVersion = 1, content = new { clinic = new { phone = "0555" } } } }),
            HttpStatusCode.BadRequest,
            "ClinicSites.Validation");
        await ShouldFailAsync(
            await SendAsync(HttpMethod.Put, $"{ctx.Url}/draft", ctx.Token, new { config = new { content = new { } } }),
            HttpStatusCode.BadRequest,
            "ClinicSites.Validation");
        await ShouldFailAsync(
            await SendAsync(HttpMethod.Put, $"{ctx.Url}/draft", ctx.Token, new { }),
            HttpStatusCode.BadRequest,
            "ClinicSites.Validation");
    }

    [Fact]
    public async Task Draft_Should_Return_400_For_Unpaired_Surrogate()
    {
        var ctx = await SeedAsync();
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{ctx.Url}/draft")
        {
            Content = new StringContent("""{"config":{"schemaVersion":1,"a":"\uD800"}}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);

        await ShouldFailAsync(await _client.SendAsync(request), HttpStatusCode.BadRequest, "ClinicSites.Validation");
    }

    [Fact]
    public async Task Draft_Should_Reject_Image_Of_Another_Site()
    {
        var mine = await SeedAsync();
        var other = await SeedAsync();
        await SaveDraftAsync(mine, Config());
        await SaveDraftAsync(other, Config());
        var foreignImage = await UploadOkAsync(other);

        var response = await SendAsync(HttpMethod.Put, $"{mine.Url}/draft", mine.Token, new
        {
            config = Config(imageId: foreignImage),
            rowVersion = await RowVersionAsync(mine),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("issues")[0].GetProperty("reason").GetString().Should().Be(ClinicSiteConfig.Reasons.ImageNotInSite);
    }

    // ---------- kiracılar arası erişim ve yetki ----------

    [Fact]
    public async Task Other_Tenant_Should_Not_Reach_Site()
    {
        var owner = await SeedAsync();
        await PublishNewSiteAsync(owner);
        var image = await UploadOkAsync(owner);
        var stranger = await SeedAsync();
        var token = stranger.Token;
        var rowVersion = await RowVersionAsync(owner);

        await ShouldFailAsync(await SendAsync(HttpMethod.Get, owner.Url, token), HttpStatusCode.NotFound, "Clinics.NotFound");
        await ShouldFailAsync(await SendAsync(HttpMethod.Put, $"{owner.Url}/draft", token, new { config = Config("ele gecirme"), rowVersion }), HttpStatusCode.NotFound, "Clinics.NotFound");
        await ShouldFailAsync(await SendAsync(HttpMethod.Post, $"{owner.Url}/publish", token, new { rowVersion }), HttpStatusCode.NotFound, "Clinics.NotFound");
        await ShouldFailAsync(await SendAsync(HttpMethod.Post, $"{owner.Url}/close", token), HttpStatusCode.NotFound, "Clinics.NotFound");
        await ShouldFailAsync(await SendAsync(HttpMethod.Put, $"{owner.Url}/address", token, new { slug = "ele-gecirilmis" }), HttpStatusCode.NotFound, "Clinics.NotFound");
        await ShouldFailAsync(await SendAsync(HttpMethod.Get, $"{owner.Url}/versions", token), HttpStatusCode.NotFound, "Clinics.NotFound");
        await ShouldFailAsync(await SendAsync(HttpMethod.Get, $"{owner.Url}/images/{image}/content", token), HttpStatusCode.NotFound, "Clinics.NotFound");
        await ShouldFailAsync(await SendAsync(HttpMethod.Delete, $"{owner.Url}/images/{image}", token), HttpStatusCode.NotFound, "Clinics.NotFound");

        // Kendi kliniğinin yolundan başka sitenin görseli de açılmaz.
        await SaveDraftAsync(stranger, Config());
        await ShouldFailAsync(await SendAsync(HttpMethod.Get, $"{stranger.Url}/images/{image}/content", token), HttpStatusCode.NotFound, "ClinicSites.ImageNotFound");
    }

    [Fact]
    public async Task Tenant_Filter_Should_Hide_Site_Rows_From_Other_Tenant()
    {
        var owner = await SeedAsync();
        await PublishNewSiteAsync(owner);
        var stranger = await SeedAsync();

        using var scope = _factory.Services.CreateScope();
        using var tenant = scope.ServiceProvider.GetRequiredService<ITenantScope>().UseTenant(stranger.TenantId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.ClinicSites.AnyAsync(s => s.TenantId == owner.TenantId)).Should().BeFalse();
        (await db.ClinicSiteVersions.AnyAsync(v => v.TenantId == owner.TenantId)).Should().BeFalse();
        (await db.ClinicSiteAddresses.AnyAsync(a => a.TenantId == owner.TenantId)).Should().BeFalse();
    }

    [Fact]
    public async Task Permissions_Should_Follow_Clinic_Settings_Policy()
    {
        var reader = await SeedAsync(permissions: [PermissionCatalog.Clinics.Read]);

        (await SendAsync(HttpMethod.Get, reader.Url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SendAsync(HttpMethod.Get, reader.Url, reader.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Put, $"{reader.Url}/draft", reader.Token, new { config = Config() })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, $"{reader.Url}/publish", reader.Token, new { rowVersion = "AAAAAAAAB9E=" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, $"{reader.Url}/close", reader.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, $"{reader.Url}/address-availability?slug=abcd", reader.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---------- adres ----------

    [Fact]
    public async Task Address_Availability_Should_Report_Reason()
    {
        var ctx = await SeedAsync();
        var taken = await PublishNewSiteAsync(await SeedAsync());
        var free = $"bos-adres-{Guid.NewGuid():N}"[..24];

        async Task<JsonElement> CheckAsync(string slug)
            => await OkJsonAsync(await SendAsync(HttpMethod.Get, $"{ctx.Url}/address-availability?slug={Uri.EscapeDataString(slug)}", ctx.Token));

        (await CheckAsync(free)).GetProperty("isAvailable").GetBoolean().Should().BeTrue();
        (await CheckAsync(free.ToUpperInvariant())).GetProperty("slug").GetString().Should().Be(free, "küçük harfe iner");
        (await CheckAsync("api")).GetProperty("reason").GetString().Should().Be("Reserved");
        (await CheckAsync("ab")).GetProperty("reason").GetString().Should().Be("Invalid");
        (await CheckAsync("gecersiz adres")).GetProperty("reason").GetString().Should().Be("Invalid");
        (await CheckAsync(taken)).GetProperty("reason").GetString().Should().Be("Taken");
    }

    [Fact]
    public async Task Old_Address_Should_Redirect_And_Not_Be_Reusable()
    {
        var owner = await SeedAsync();
        var oldSlug = await PublishNewSiteAsync(owner);
        var newSlug = $"yeni-adres-{Guid.NewGuid():N}"[..26];

        (await OkJsonAsync(await SendAsync(HttpMethod.Put, $"{owner.Url}/address", owner.Token, new { slug = newSlug })))
            .GetProperty("slug").GetString().Should().Be(newSlug);

        var redirect = await PublicJsonAsync(oldSlug);
        redirect.GetProperty("status").GetString().Should().Be("Redirect");
        redirect.GetProperty("redirectToSlug").GetString().Should().Be(newSlug);
        redirect.GetProperty("config").ValueKind.Should().Be(JsonValueKind.Null);
        redirect.GetProperty("clinic").ValueKind.Should().Be(JsonValueKind.Null);

        (await PublicJsonAsync(newSlug)).GetProperty("status").GetString().Should().Be("Published");

        var other = await SeedAsync();
        await SaveDraftAsync(other, Config());
        (await OkJsonAsync(await SendAsync(HttpMethod.Get, $"{other.Url}/address-availability?slug={oldSlug}", other.Token)))
            .GetProperty("reason").GetString().Should().Be("Taken");
        await ShouldFailAsync(
            await SendAsync(HttpMethod.Put, $"{other.Url}/address", other.Token, new { slug = oldSlug }),
            HttpStatusCode.Conflict,
            "ClinicSites.SlugTaken");
    }

    [Fact]
    public async Task Redirect_Should_Always_Point_To_Current_Address_And_Site_May_Reclaim_Old_One()
    {
        var ctx = await SeedAsync();
        var first = await PublishNewSiteAsync(ctx);
        var second = $"ikinci-{Guid.NewGuid():N}"[..22];
        var third = $"ucuncu-{Guid.NewGuid():N}"[..22];

        await OkJsonAsync(await SendAsync(HttpMethod.Put, $"{ctx.Url}/address", ctx.Token, new { slug = second }));
        await OkJsonAsync(await SendAsync(HttpMethod.Put, $"{ctx.Url}/address", ctx.Token, new { slug = third }));

        (await PublicJsonAsync(first)).GetProperty("redirectToSlug").GetString().Should().Be(third, "zincirleme yönlenme olmaz");
        (await PublicJsonAsync(second)).GetProperty("redirectToSlug").GetString().Should().Be(third);

        await OkJsonAsync(await SendAsync(HttpMethod.Put, $"{ctx.Url}/address", ctx.Token, new { slug = first }));

        (await PublicJsonAsync(first)).GetProperty("status").GetString().Should().Be("Published");
        (await PublicJsonAsync(third)).GetProperty("redirectToSlug").GetString().Should().Be(first);
    }

    [Fact]
    public async Task Unpublished_Address_Should_Be_Released_On_Change()
    {
        var ctx = await SeedAsync();
        var initial = (await SaveDraftAsync(ctx, Config())).GetProperty("slug").GetString()!;
        var chosen = $"secilen-{Guid.NewGuid():N}"[..24];

        await OkJsonAsync(await SendAsync(HttpMethod.Put, $"{ctx.Url}/address", ctx.Token, new { slug = chosen }));

        var other = await SeedAsync();
        (await OkJsonAsync(await SendAsync(HttpMethod.Get, $"{other.Url}/address-availability?slug={initial}", other.Token)))
            .GetProperty("isAvailable").GetBoolean().Should().BeTrue("yayınlanmamış adres hiç paylaşılmadı");
    }

    [Theory]
    [InlineData("api")]
    [InlineData("Gecersiz Adres!")]
    [InlineData("ab")]
    public async Task Address_Change_Should_Reject_Reserved_And_Invalid(string slug)
    {
        var ctx = await SeedAsync();
        await SaveDraftAsync(ctx, Config());

        await ShouldFailAsync(
            await SendAsync(HttpMethod.Put, $"{ctx.Url}/address", ctx.Token, new { slug }),
            HttpStatusCode.BadRequest,
            "ClinicSites.Validation");
    }

    [Fact]
    public async Task Address_Index_Should_Reject_Duplicate_Across_Tenants()
    {
        var a = await SeedAsync();
        var slug = await PublishNewSiteAsync(a);
        var b = await SeedAsync();
        await SaveDraftAsync(b, Config());
        Guid siteId = default;
        await WithDbAsync(async db => siteId = (await db.ClinicSites.SingleAsync(s => s.ClinicId == b.ClinicId)).Id);

        await WithDbAsync(async db =>
        {
            db.ClinicSiteAddresses.Add(ClinicSiteAddress.Create(b.TenantId, siteId, slug, DateTime.UtcNow));
            await FluentActions.Awaiting(() => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        });
    }

    [Fact]
    public async Task Old_Address_Of_Closed_Site_Should_Look_Closed_And_Serve_No_Image()
    {
        var ctx = await SeedAsync();
        await SaveDraftAsync(ctx, Config());
        var image = await UploadOkAsync(ctx);
        await UpdateDraftAsync(ctx, Config(imageId: image));
        var oldSlug = (await PublishAsync(ctx)).GetProperty("slug").GetString()!;
        var newSlug = $"yeni-yer-{Guid.NewGuid():N}"[..24];
        await OkJsonAsync(await SendAsync(HttpMethod.Put, $"{ctx.Url}/address", ctx.Token, new { slug = newSlug }));
        var imageUrl = $"/api/v1/public/sites/{oldSlug}/images/{image}";

        (await PublicJsonAsync(oldSlug)).GetProperty("status").GetString().Should().Be("Redirect");
        (await _client.GetAsync(imageUrl)).StatusCode.Should().Be(HttpStatusCode.OK, "açık sitede eski adres görsel sunar");

        async Task ShouldLookClosedAsync()
        {
            var body = await (await PublicAsync(oldSlug)).Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(body).RootElement;
            json.GetProperty("status").GetString().Should().Be("Closed");
            json.GetProperty("redirectToSlug").ValueKind.Should().Be(JsonValueKind.Null);
            body.Should().NotContain(newSlug, "kapalı site yeni adresini açıklamaz");
            (await _client.GetAsync(imageUrl)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        await OkJsonAsync(await SendAsync(HttpMethod.Post, $"{ctx.Url}/close", ctx.Token));
        await ShouldLookClosedAsync();
        await OkJsonAsync(await SendAsync(HttpMethod.Post, $"{ctx.Url}/reopen", ctx.Token));
        (await PublicJsonAsync(oldSlug)).GetProperty("redirectToSlug").GetString().Should().Be(newSlug);

        await ExpireTrialAsync(ctx.TenantId);
        await ShouldLookClosedAsync();

        await PayAsync(ctx.TenantId);
        (await PublicJsonAsync(oldSlug)).GetProperty("redirectToSlug").GetString().Should().Be(newSlug);
        (await _client.GetAsync(imageUrl)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Hakları harcayıp son adresi döner.</summary>
    private async Task<string> SpendAddressChangesAsync(Ctx ctx)
    {
        var last = string.Empty;
        for (var i = 0; i < ClinicSite.IncludedAddressChanges; i++)
        {
            last = $"hak-{i}-{Guid.NewGuid():N}"[..20];
            var state = await OkJsonAsync(await SendAsync(HttpMethod.Put, $"{ctx.Url}/address", ctx.Token, new { slug = last }));
            state.GetProperty("addressChangesRemaining").GetInt32().Should().Be(ClinicSite.IncludedAddressChanges - i - 1);
        }

        return last;
    }

    [Fact]
    public async Task Address_Changes_Should_Stop_After_Allowance_And_Refer_To_Support()
    {
        var ctx = await SeedAsync();
        var first = await PublishNewSiteAsync(ctx);
        var detail = await OkJsonAsync(await SendAsync(HttpMethod.Get, ctx.Url, ctx.Token));
        detail.GetProperty("site").GetProperty("addressChangesRemaining").GetInt32().Should().Be(ClinicSite.IncludedAddressChanges);

        var last = await SpendAddressChangesAsync(ctx);

        var blocked = $"fazla-{Guid.NewGuid():N}"[..20];
        var response = await SendAsync(HttpMethod.Put, $"{ctx.Url}/address", ctx.Token, new { slug = blocked });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be(ClinicSite.AddressChangeLimitReachedCode);
        problem.GetProperty("detail").GetString().Should().Contain("destek");
        problem.GetProperty("addressChangesRemaining").GetInt32().Should().Be(0);

        // Kendi eski adresine dönüş de bir değişikliktir.
        await ShouldFailAsync(
            await SendAsync(HttpMethod.Put, $"{ctx.Url}/address", ctx.Token, new { slug = first }),
            HttpStatusCode.Conflict,
            ClinicSite.AddressChangeLimitReachedCode);

        // Reddedilen değişiklik yarım kalmaz; eski adresler serbest kalmaz.
        (await PublicJsonAsync(last)).GetProperty("status").GetString().Should().Be("Published");
        (await PublicJsonAsync(first)).GetProperty("redirectToSlug").GetString().Should().Be(last);
        var other = await SeedAsync();
        (await OkJsonAsync(await SendAsync(HttpMethod.Get, $"{other.Url}/address-availability?slug={blocked}", other.Token)))
            .GetProperty("isAvailable").GetBoolean().Should().BeTrue();
        (await OkJsonAsync(await SendAsync(HttpMethod.Get, $"{other.Url}/address-availability?slug={first}", other.Token)))
            .GetProperty("reason").GetString().Should().Be("Taken");
    }

    [Fact]
    public async Task Unpublished_Address_Changes_Should_Not_Spend_Allowance()
    {
        var ctx = await SeedAsync();
        await SaveDraftAsync(ctx, Config());

        for (var i = 0; i < ClinicSite.IncludedAddressChanges + 1; i++)
        {
            var state = await OkJsonAsync(await SendAsync(HttpMethod.Put, $"{ctx.Url}/address", ctx.Token, new { slug = $"taslak-{i}-{Guid.NewGuid():N}"[..20] }));
            state.GetProperty("addressChangesRemaining").GetInt32().Should().Be(ClinicSite.IncludedAddressChanges);
        }
    }

    private async Task<string> PlatformAdminTokenAsync()
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _, _) = jwt.Create(
            Guid.NewGuid(),
            $"destek-{Guid.NewGuid():N}@example.com",
            Array.Empty<string>(),
            [new Claim(VeterinerClaims.PlatformAdmin, bool.TrueString)]);
        return token;
    }

    [Fact]
    public async Task Platform_Admin_Should_Grant_Address_Changes_By_Current_Or_Old_Address_And_Audit()
    {
        var ctx = await SeedAsync();
        var first = await PublishNewSiteAsync(ctx);
        await SpendAddressChangesAsync(ctx);
        var admin = await PlatformAdminTokenAsync();

        // Abonelik salt okunur olsa da destek işlemi kiracının yazma kapısına takılmaz.
        await ExpireTrialAsync(ctx.TenantId);
        var granted = await OkJsonAsync(await SendAsync(
            HttpMethod.Post, $"/api/v1/admin/clinic-sites/{first}/address-change-grants", admin, new { additionalChanges = 1 }));
        granted.GetProperty("addressChangesRemaining").GetInt32().Should().Be(1);
        await PayAsync(ctx.TenantId);

        var slug = $"destekle-{Guid.NewGuid():N}"[..20];
        (await OkJsonAsync(await SendAsync(HttpMethod.Put, $"{ctx.Url}/address", ctx.Token, new { slug })))
            .GetProperty("addressChangesRemaining").GetInt32().Should().Be(0);

        await WithDbAsync(async db =>
        {
            var target = $"Slug={first};Additional=1";
            (await db.AuditLogs.AsNoTracking().AnyAsync(a => a.Action == "ClinicSite.AddressChanges.Grant" && a.TargetId == target && a.Success))
                .Should().BeTrue();
        });
    }

    [Fact]
    public async Task Address_Change_Grant_Should_Be_Platform_Admin_Only_And_Validated()
    {
        var ctx = await SeedAsync();
        var slug = await PublishNewSiteAsync(ctx);
        var url = $"/api/v1/admin/clinic-sites/{slug}/address-change-grants";
        var admin = await PlatformAdminTokenAsync();

        (await SendAsync(HttpMethod.Post, url, body: new { additionalChanges = 1 })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SendAsync(HttpMethod.Post, url, ctx.Token, new { additionalChanges = 1 })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "klinik yöneticisi kendine hak veremez");
        (await SendAsync(HttpMethod.Post, url, admin, new { additionalChanges = 0 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Post, url, admin, new { additionalChanges = 4 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Post, "/api/v1/admin/clinic-sites/yok-boyle-adres/address-change-grants", admin, new { additionalChanges = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("yok-boyle-bir-adres")]
    [InlineData("ab")]
    [InlineData("api")]
    public async Task Public_Should_Return_404_For_Unknown_Address(string slug)
        => await ShouldFailAsync(await PublicAsync(slug), HttpStatusCode.NotFound, "PublicSites.NotFound");

    // ---------- sürümler ----------

    [Fact]
    public async Task Versions_Should_Be_Immutable_And_Restore_Should_Only_Touch_Draft()
    {
        var ctx = await SeedAsync();
        var slug = await PublishNewSiteAsync(ctx, Config("birinci"));
        await UpdateDraftAsync(ctx, Config("ikinci"));
        await PublishAsync(ctx);
        await UpdateDraftAsync(ctx, Config("taslak"));

        var versions = await OkJsonAsync(await SendAsync(HttpMethod.Get, $"{ctx.Url}/versions", ctx.Token));
        versions.GetArrayLength().Should().Be(2);
        versions[0].GetProperty("versionNumber").GetInt32().Should().Be(2);
        versions[0].GetProperty("isPublished").GetBoolean().Should().BeTrue();
        versions[0].GetProperty("publishedByName").GetString().Should().NotBeNullOrEmpty();
        versions[1].GetProperty("isPublished").GetBoolean().Should().BeFalse();
        versions[0].TryGetProperty("config", out _).Should().BeFalse("liste yapılandırma taşımaz");

        var restored = await OkJsonAsync(await SendAsync(
            HttpMethod.Post, $"{ctx.Url}/versions/1/restore", ctx.Token, new { rowVersion = await RowVersionAsync(ctx) }));
        restored.GetProperty("publishedVersionNumber").GetInt32().Should().Be(2);
        restored.GetProperty("hasUnpublishedChanges").GetBoolean().Should().BeTrue();

        var detail = await OkJsonAsync(await SendAsync(HttpMethod.Get, ctx.Url, ctx.Token));
        detail.GetProperty("draft").GetProperty("content").GetProperty("clinic").GetProperty("slogan").GetString().Should().Be("birinci");

        var published = await PublicJsonAsync(slug);
        published.GetProperty("versionNumber").GetInt32().Should().Be(2);
        published.GetProperty("config").GetProperty("content").GetProperty("clinic").GetProperty("slogan").GetString().Should().Be("ikinci");

        // Dönüş taslağı yayınlanınca yeni sürüm olur; eski satırlar değişmez.
        await PublishAsync(ctx);
        var afterRestore = await PublicJsonAsync(slug);
        afterRestore.GetProperty("versionNumber").GetInt32().Should().Be(3);
        afterRestore.GetProperty("config").GetProperty("content").GetProperty("clinic").GetProperty("slogan").GetString().Should().Be("birinci");

        await WithDbAsync(async db =>
        {
            var siteId = (await db.ClinicSites.SingleAsync(s => s.ClinicId == ctx.ClinicId)).Id;
            var numbers = await db.ClinicSiteVersions.Where(v => v.SiteId == siteId).OrderBy(v => v.VersionNumber).Select(v => v.VersionNumber).ToListAsync();
            numbers.Should().Equal(1, 2, 3);
        });
    }

    [Fact]
    public async Task Version_Row_Should_Reject_Field_Change_At_Persistence_Level()
    {
        var ctx = await SeedAsync();
        await PublishNewSiteAsync(ctx);

        await WithDbAsync(async db =>
        {
            var siteId = (await db.ClinicSites.SingleAsync(s => s.ClinicId == ctx.ClinicId)).Id;
            var version = await db.ClinicSiteVersions.SingleAsync(v => v.SiteId == siteId);

            db.Entry(version).Property(v => v.ConfigJson).CurrentValue = """{"schemaVersion":1,"kurcalandi":true}""";

            await FluentActions.Awaiting(() => db.SaveChangesAsync()).Should().ThrowAsync<InvalidOperationException>();
        });
    }

    [Fact]
    public async Task Restore_Should_Return_404_For_Unknown_Version()
    {
        var ctx = await SeedAsync();
        await PublishNewSiteAsync(ctx);

        await ShouldFailAsync(
            await SendAsync(HttpMethod.Post, $"{ctx.Url}/versions/99/restore", ctx.Token, new { rowVersion = await RowVersionAsync(ctx) }),
            HttpStatusCode.NotFound,
            "ClinicSites.VersionNotFound");
    }

    [Fact]
    public async Task Publish_Should_Keep_Only_Retained_Version_Count()
    {
        var ctx = await SeedAsync();
        var slug = await PublishNewSiteAsync(ctx, Config("surum-1"));
        for (var i = 2; i <= ClinicSite.MaxRetainedVersions + 2; i++)
        {
            await UpdateDraftAsync(ctx, Config($"surum-{i}"));
            await PublishAsync(ctx);
        }

        var versions = await OkJsonAsync(await SendAsync(HttpMethod.Get, $"{ctx.Url}/versions", ctx.Token));

        versions.GetArrayLength().Should().Be(ClinicSite.MaxRetainedVersions);
        versions[0].GetProperty("versionNumber").GetInt32().Should().Be(ClinicSite.MaxRetainedVersions + 2);
        versions[ClinicSite.MaxRetainedVersions - 1].GetProperty("versionNumber").GetInt32().Should().Be(3);
        (await PublicJsonAsync(slug)).GetProperty("versionNumber").GetInt32().Should().Be(ClinicSite.MaxRetainedVersions + 2);
    }

    // ---------- görseller ----------

    [Theory]
    [InlineData("<html><script>alert(1)</script></html>", "kedi.jpg", "image/jpeg")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"/>", "kedi.svg", "image/svg+xml")]
    [InlineData("GIF89a aaaaaaaaaaaa", "kedi.gif", "image/gif")]
    public async Task Upload_Should_Reject_By_Signature_Not_By_Declared_Type(string content, string fileName, string declaredType)
    {
        var ctx = await SeedAsync();
        await SaveDraftAsync(ctx, Config());

        await ShouldFailAsync(
            await UploadAsync(ctx, Encoding.UTF8.GetBytes(content), declaredType, fileName),
            HttpStatusCode.UnsupportedMediaType,
            "Files.UnsupportedType");
    }

    [Fact]
    public async Task Upload_Should_Reject_Pdf_Even_Though_Patient_Files_Allow_It()
    {
        var ctx = await SeedAsync();
        await SaveDraftAsync(ctx, Config());

        await ShouldFailAsync(await UploadAsync(ctx, Pdf, "image/jpeg", "kedi.jpg"), HttpStatusCode.UnsupportedMediaType, "Files.UnsupportedType");
    }

    [Fact]
    public async Task Upload_Should_Reject_Oversize_And_Require_Site()
    {
        var ctx = await SeedAsync();
        await ShouldFailAsync(await UploadAsync(ctx, Jpeg), HttpStatusCode.NotFound, "ClinicSites.NotFound");

        await SaveDraftAsync(ctx, Config());
        var big = new byte[ClinicSiteImage.MaxSizeBytes + 1];
        Jpeg.CopyTo(big, 0);

        await ShouldFailAsync(await UploadAsync(ctx, big), HttpStatusCode.RequestEntityTooLarge, "Files.TooLarge");
    }

    [Fact]
    public async Task Upload_Should_Detect_Type_From_Content_And_List_On_Detail()
    {
        var ctx = await SeedAsync();
        await SaveDraftAsync(ctx, Config());

        var response = await UploadAsync(ctx, Png, "image/jpeg", "kedi.jpg");
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var image = await response.Content.ReadFromJsonAsync<JsonElement>();
        image.GetProperty("contentType").GetString().Should().Be("image/png");
        image.GetProperty("sizeBytes").GetInt64().Should().Be(Png.Length);

        var detail = await OkJsonAsync(await SendAsync(HttpMethod.Get, ctx.Url, ctx.Token));
        detail.GetProperty("images")[0].GetProperty("id").GetString().Should().Be(image.GetProperty("id").GetString());

        var preview = await SendAsync(HttpMethod.Get, $"{ctx.Url}/images/{image.GetProperty("id").GetString()}/content", ctx.Token);
        preview.StatusCode.Should().Be(HttpStatusCode.OK);
        preview.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        preview.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await preview.Content.ReadAsByteArrayAsync()).Should().Equal(Png);
    }

    [Fact]
    public async Task Public_Image_Should_Serve_Only_What_Published_Version_References()
    {
        var ctx = await SeedAsync();
        var slug = (await SaveDraftAsync(ctx, Config())).GetProperty("slug").GetString()!;
        var published = await UploadOkAsync(ctx);
        var draftOnly = await UploadOkAsync(ctx);
        var unreferenced = await UploadOkAsync(ctx);

        await UpdateDraftAsync(ctx, Config(imageId: published));
        await PublishAsync(ctx);
        await UpdateDraftAsync(ctx, Config(imageId: draftOnly));

        var ok = await _client.GetAsync($"/api/v1/public/sites/{slug}/images/{published}");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        ok.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
        ok.Content.Headers.ContentDisposition!.DispositionType.Should().Be("inline");
        ok.Headers.ETag.Should().NotBeNull();
        ok.Headers.CacheControl!.Public.Should().BeTrue();
        ok.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        (await ok.Content.ReadAsByteArrayAsync()).Should().Equal(Jpeg);

        using var conditional = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/public/sites/{slug}/images/{published}");
        conditional.Headers.IfNoneMatch.Add(ok.Headers.ETag!);
        (await _client.SendAsync(conditional)).StatusCode.Should().Be(HttpStatusCode.NotModified);

        (await _client.GetAsync($"/api/v1/public/sites/{slug}/images/{draftOnly}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "taslak görseli açık değil");
        (await _client.GetAsync($"/api/v1/public/sites/{slug}/images/{unreferenced}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.GetAsync($"/api/v1/public/sites/{slug}/images/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Başka sitenin adresinden de açılmaz.
        var otherSlug = await PublishNewSiteAsync(await SeedAsync());
        (await _client.GetAsync($"/api/v1/public/sites/{otherSlug}/images/{published}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Patient_File_Id_Should_Not_Open_Through_Public_Image_Route()
    {
        var ctx = await SeedAsync();
        var slug = await PublishNewSiteAsync(ctx);

        // Aynı kliniğin hazır hasta dosyası (K12: StoredFile kuralı değişmez, bu yoldan açılmaz).
        var patientFile = new StoredFile(
            ctx.TenantId, ctx.ClinicId, StoredFileOwnerType.ImagingRecord, Guid.NewGuid(), "rontgen.jpg", StoredFileType.Jpeg, Guid.NewGuid(), DateTime.UtcNow);
        patientFile.MarkReady(Jpeg.Length, new string('a', StoredFile.Sha256HexLength), DateTime.UtcNow);
        await WithDbAsync(async db =>
        {
            db.StoredFiles.Add(patientFile);
            await db.SaveChangesAsync();
        });

        (await _client.GetAsync($"/api/v1/public/sites/{slug}/images/{patientFile.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Get, $"{ctx.Url}/images/{patientFile.Id}/content", ctx.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var response = await SendAsync(HttpMethod.Put, $"{ctx.Url}/draft", ctx.Token, new
        {
            config = Config(imageId: patientFile.Id.ToString()),
            rowVersion = await RowVersionAsync(ctx),
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "hasta dosyası site görseli olarak başvurulamaz");
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("issues")[0].GetProperty("reason").GetString()
            .Should().Be(ClinicSiteConfig.Reasons.ImageNotInSite);
    }

    [Fact]
    public async Task Delete_Image_Should_Be_Blocked_While_In_Use()
    {
        var ctx = await SeedAsync();
        await SaveDraftAsync(ctx, Config());
        var image = await UploadOkAsync(ctx);
        await UpdateDraftAsync(ctx, Config(imageId: image));

        var inDraft = await SendAsync(HttpMethod.Delete, $"{ctx.Url}/images/{image}", ctx.Token);
        await ShouldFailAsync(inDraft, HttpStatusCode.Conflict, "ClinicSites.ImageInUse");

        await PublishAsync(ctx);
        await UpdateDraftAsync(ctx, Config());

        var inPublished = await SendAsync(HttpMethod.Delete, $"{ctx.Url}/images/{image}", ctx.Token);
        inPublished.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await inPublished.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("usedIn")[0].GetString().Should().Be("published");
    }

    [Fact]
    public async Task Delete_Image_Should_Change_RowVersion_So_Racing_Writes_Conflict()
    {
        var ctx = await SeedAsync();
        await SaveDraftAsync(ctx, Config());
        var image = await UploadOkAsync(ctx);
        var before = await RowVersionAsync(ctx);

        var state = await OkJsonAsync(await SendAsync(HttpMethod.Delete, $"{ctx.Url}/images/{image}", ctx.Token));

        // Görsel silinmeden önce okunmuş sürümle yapılan yayın ya da taslak kaydı silinen görseli canlıya taşıyamaz.
        state.GetProperty("rowVersion").GetString().Should().NotBe(before);
        await ShouldFailAsync(
            await SendAsync(HttpMethod.Post, $"{ctx.Url}/publish", ctx.Token, new { rowVersion = before }),
            HttpStatusCode.Conflict,
            "ClinicSites.ConcurrencyConflict");
        await ShouldFailAsync(
            await SendAsync(HttpMethod.Put, $"{ctx.Url}/draft", ctx.Token, new { config = Config(imageId: image), rowVersion = before }),
            HttpStatusCode.Conflict,
            "ClinicSites.ConcurrencyConflict");
    }

    [Fact]
    public async Task Restored_Draft_With_Deleted_Image_Should_Save_But_Not_Publish()
    {
        var ctx = await SeedAsync();
        await SaveDraftAsync(ctx, Config());
        var image = await UploadOkAsync(ctx);
        await UpdateDraftAsync(ctx, Config(imageId: image));
        await PublishAsync(ctx);
        await UpdateDraftAsync(ctx, Config("gorselsiz"));
        await PublishAsync(ctx);

        await OkJsonAsync(await SendAsync(HttpMethod.Delete, $"{ctx.Url}/images/{image}", ctx.Token));
        (await SendAsync(HttpMethod.Get, $"{ctx.Url}/images/{image}/content", ctx.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await OkJsonAsync(await SendAsync(HttpMethod.Post, $"{ctx.Url}/versions/1/restore", ctx.Token, new { rowVersion = await RowVersionAsync(ctx) }));

        // Taslak kilitlenmez: otomatik kayıt çalışır, yalnız yayın engellenir.
        await UpdateDraftAsync(ctx, Config("duzeltiliyor", image));
        var detail = await OkJsonAsync(await SendAsync(HttpMethod.Get, ctx.Url, ctx.Token));
        detail.GetProperty("missingImageIds")[0].GetString().Should().Be(image);

        var publish = await SendAsync(HttpMethod.Post, $"{ctx.Url}/publish", ctx.Token, new { rowVersion = await RowVersionAsync(ctx) });
        publish.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await publish.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("ClinicSites.PublishRequirementsNotMet");
        problem.GetProperty("missingImageIds")[0].GetString().Should().Be(image);
    }

    [Fact]
    public async Task Trial_Storage_Quota_Should_Count_Site_Images()
    {
        var ctx = await SeedAsync();
        await SaveDraftAsync(ctx, Config());
        var image = await UploadOkAsync(ctx);
        await WithDbAsync(db => db.ClinicSiteImages.Where(i => i.Id == Guid.Parse(image))
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.SizeBytes, TrialLimits.MaxStorageBytes)));

        var response = await UploadAsync(ctx, Jpeg);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await response.Content.ReadAsStringAsync());
    }

    // ---------- geçici kapatma ve abonelik ----------

    [Fact]
    public async Task Close_Should_Hide_Site_And_Reopen_Should_Restore_It()
    {
        var ctx = await SeedAsync();
        var slug = await PublishNewSiteAsync(ctx);

        var closed = await OkJsonAsync(await SendAsync(HttpMethod.Post, $"{ctx.Url}/close", ctx.Token));
        closed.GetProperty("isTemporarilyClosed").GetBoolean().Should().BeTrue();

        var json = await PublicJsonAsync(slug);
        json.GetProperty("status").GetString().Should().Be("Closed");
        json.GetProperty("message").GetString().Should().NotBeNullOrEmpty();
        json.GetProperty("config").ValueKind.Should().Be(JsonValueKind.Null);
        json.GetProperty("clinic").ValueKind.Should().Be(JsonValueKind.Null);
        json.GetProperty("onlineBooking").ValueKind.Should().Be(JsonValueKind.Null);

        await OkJsonAsync(await SendAsync(HttpMethod.Post, $"{ctx.Url}/reopen", ctx.Token));
        (await PublicJsonAsync(slug)).GetProperty("status").GetString().Should().Be("Published");
    }

    [Fact]
    public async Task Publish_And_Close_Should_Write_Audit_Log()
    {
        var ctx = await SeedAsync();
        await PublishNewSiteAsync(ctx);
        await OkJsonAsync(await SendAsync(HttpMethod.Post, $"{ctx.Url}/close", ctx.Token));
        await OkJsonAsync(await SendAsync(HttpMethod.Post, $"{ctx.Url}/reopen", ctx.Token));

        await WithDbAsync(async db =>
        {
            var target = $"ClinicId={ctx.ClinicId}";
            var actions = await db.AuditLogs.AsNoTracking()
                .Where(a => a.TargetId != null && a.TargetId.Contains(target) && a.Success)
                .Select(a => a.Action)
                .ToListAsync();
            actions.Should().Contain(["ClinicSite.Publish", "ClinicSite.Close", "ClinicSite.Reopen"]);
        });
    }

    [Fact]
    public async Task Read_Only_Subscription_Should_Close_Site_Identically_And_Payment_Should_Restore_It()
    {
        var ctx = await SeedAsync();
        await PublishNewSiteAsync(ctx);
        var image = await UploadOkAsync(ctx);
        await UpdateDraftAsync(ctx, Config(imageId: image));
        var slug = (await PublishAsync(ctx)).GetProperty("slug").GetString()!;
        await EnableBookingAsync(ctx);

        (await _client.GetAsync($"/api/v1/public/sites/{slug}/images/{image}")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Elle kapatma ile abonelik kapanması dışarıdan aynı görünmeli.
        await OkJsonAsync(await SendAsync(HttpMethod.Post, $"{ctx.Url}/close", ctx.Token));
        var byClinic = await (await PublicAsync(slug)).Content.ReadAsStringAsync();
        await OkJsonAsync(await SendAsync(HttpMethod.Post, $"{ctx.Url}/reopen", ctx.Token));

        await ExpireTrialAsync(ctx.TenantId);

        var response = await PublicAsync(slug);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        body.Should().Be(byClinic, "kapanma nedeni dışarıdan ayırt edilemez");
        body.Should().NotContainEquivalentOf("abonelik").And.NotContainEquivalentOf("subscription").And.NotContainEquivalentOf("trial");
        (await _client.GetAsync($"/api/v1/public/sites/{slug}/images/{image}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Veri silinmez; panel görür ve kapanmanın nedenini bilir, yazma kapısı komutları durdurur.
        var detail = await OkJsonAsync(await SendAsync(HttpMethod.Get, ctx.Url, ctx.Token));
        detail.GetProperty("isClosedBySubscription").GetBoolean().Should().BeTrue();
        detail.GetProperty("site").GetProperty("isTemporarilyClosed").GetBoolean().Should().BeFalse();
        detail.GetProperty("images").GetArrayLength().Should().Be(1);
        (await OkJsonAsync(await SendAsync(HttpMethod.Get, $"{ctx.Url}/versions", ctx.Token))).GetArrayLength().Should().Be(2);
        await ShouldFailAsync(
            await SendAsync(HttpMethod.Post, $"{ctx.Url}/publish", ctx.Token, new { rowVersion = await RowVersionAsync(ctx) }),
            HttpStatusCode.Forbidden,
            "Subscriptions.TenantReadOnly");

        // Ödeme: son yayınlanan sürüm kendiliğinden döner.
        await PayAsync(ctx.TenantId);

        var restored = await PublicJsonAsync(slug);
        restored.GetProperty("status").GetString().Should().Be("Published");
        restored.GetProperty("versionNumber").GetInt32().Should().Be(2);
        restored.GetProperty("onlineBooking").GetProperty("isEnabled").GetBoolean().Should().BeTrue();
        (await _client.GetAsync($"/api/v1/public/sites/{slug}/images/{image}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Cancelled_Subscription_Should_Close_Site()
    {
        var ctx = await SeedAsync();
        var slug = await PublishNewSiteAsync(ctx);

        await SetSubscriptionStatusAsync(ctx.TenantId, TenantSubscriptionStatus.Cancelled);

        var json = await PublicJsonAsync(slug);
        json.GetProperty("status").GetString().Should().Be("Closed");
        json.GetProperty("clinic").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Manually_Closed_Site_Should_Stay_Closed_After_Payment()
    {
        var ctx = await SeedAsync();
        var slug = await PublishNewSiteAsync(ctx);
        await OkJsonAsync(await SendAsync(HttpMethod.Post, $"{ctx.Url}/close", ctx.Token));
        await ExpireTrialAsync(ctx.TenantId);
        (await PublicJsonAsync(slug)).GetProperty("status").GetString().Should().Be("Closed");

        await PayAsync(ctx.TenantId);

        (await PublicJsonAsync(slug)).GetProperty("status").GetString().Should().Be("Closed", "klinik kendisi kapattı");
    }

    [Fact]
    public async Task Inactive_Tenant_Or_Clinic_Should_Return_404()
    {
        var a = await SeedAsync();
        var slugA = await PublishNewSiteAsync(a);
        await WithDbAsync(db => db.Tenants.Where(t => t.Id == a.TenantId).ExecuteUpdateAsync(s => s.SetProperty(t => t.IsActive, false)));
        await ShouldFailAsync(await PublicAsync(slugA), HttpStatusCode.NotFound, "PublicSites.NotFound");

        var b = await SeedAsync();
        var slugB = await PublishNewSiteAsync(b);
        await WithDbAsync(db => db.Clinics.Where(c => c.Id == b.ClinicId).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false)));
        await ShouldFailAsync(await PublicAsync(slugB), HttpStatusCode.NotFound, "PublicSites.NotFound");
    }

    // ---------- randevu ----------

    private async Task<string> EnableBookingAsync(Ctx ctx)
    {
        var settings = await OkJsonAsync(await SendAsync(HttpMethod.Put, $"/api/v1/clinics/{ctx.ClinicId}/online-booking", ctx.Token, new
        {
            isEnabled = true,
            minLeadMinutes = 60,
            maxAdvanceDays = 30,
        }));
        return settings.GetProperty("bookingCode").GetString()!;
    }

    [Fact]
    public async Task Online_Booking_Should_Follow_Clinic_Setting()
    {
        var ctx = await SeedAsync();
        var slug = await PublishNewSiteAsync(ctx);

        var off = (await PublicJsonAsync(slug)).GetProperty("onlineBooking");
        off.GetProperty("isEnabled").GetBoolean().Should().BeFalse();
        off.GetProperty("bookingCode").ValueKind.Should().Be(JsonValueKind.Null);
        off.GetProperty("bookingUrl").ValueKind.Should().Be(JsonValueKind.Null);

        var code = await EnableBookingAsync(ctx);

        var on = (await PublicJsonAsync(slug)).GetProperty("onlineBooking");
        on.GetProperty("isEnabled").GetBoolean().Should().BeTrue();
        on.GetProperty("bookingCode").GetString().Should().Be(code);
        on.GetProperty("bookingUrl").GetString().Should().EndWith($"/randevu/{code}");
    }

    // ---------- hız sınırı ----------

    [Fact]
    public void Public_Endpoints_Should_Be_Anonymous_And_Bound_To_Rate_Limit_Policy()
    {
        var type = typeof(PublicSitesController);

        type.GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
        type.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName.Should().Be(RateLimitingExtensions.PublicSitePolicy);
        type.GetCustomAttribute<DisableRateLimitingAttribute>().Should().BeNull();

        type.GetMethod(nameof(PublicSitesController.GetImage))!.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName
            .Should().Be(RateLimitingExtensions.PublicSiteImagePolicy);
    }

    [Fact]
    public async Task Public_Image_Endpoint_Should_Have_Own_Higher_Limit_Outside_Global_Limiter()
    {
        await using var limited = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:Enabled"] = "true" })));
        var client = limited.CreateClient();
        var url = $"/api/v1/public/sites/ab/images/{Guid.NewGuid()}";

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 605; i++)
            statuses.Add((await client.GetAsync(url)).StatusCode);

        statuses.Take(600).Should().OnlyContain(s => s == HttpStatusCode.NotFound, "genel 200/dk tavanı görsele uygulanmaz");
        statuses.Skip(600).Should().Contain(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public void Known_Proxies_Should_Be_Empty_By_Default_And_Configurable()
    {
        ForwardedHeadersOptions Apply(params string[] proxies)
        {
            var options = new ForwardedHeadersOptions();
            options.KnownProxies.Clear();
            var values = proxies.Select((p, i) => new KeyValuePair<string, string?>($"{ForwardedHeadersConfiguration.KnownProxiesKey}:{i}", p));
            ForwardedHeadersConfiguration.Apply(options, new ConfigurationBuilder().AddInMemoryCollection(values).Build());
            return options;
        }

        Apply().KnownProxies.Should().BeEmpty("ayar yoksa çerçevenin loopback varsayılanına bir şey eklenmez");
        new ForwardedHeadersOptions().KnownProxies.Should().OnlyContain(ip => IPAddress.IsLoopback(ip));

        Apply("10.0.0.5", "2001:db8::1").KnownProxies.Should().BeEquivalentTo([IPAddress.Parse("10.0.0.5"), IPAddress.Parse("2001:db8::1")]);

        FluentActions.Invoking(() => Apply("vekil.example.com")).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Public_Endpoint_Should_Return_429_When_Limit_Exceeded()
    {
        await using var limited = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:Enabled"] = "true" })));
        var client = limited.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 125; i++)
            statuses.Add((await client.GetAsync("/api/v1/public/sites/ab")).StatusCode);

        statuses.Take(120).Should().OnlyContain(s => s == HttpStatusCode.NotFound);
        statuses.Skip(120).Should().Contain(HttpStatusCode.TooManyRequests);
    }
}
