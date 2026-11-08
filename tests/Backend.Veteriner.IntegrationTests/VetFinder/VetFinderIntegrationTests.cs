using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Api.Controllers;
using Backend.Veteriner.Api.Middleware;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.VetFinder;

/// <summary>
/// Veteriner bul (TIER-015, ADR-047): katılım varsayılan kapalı ve kapatınca hemen çıkar; pasif klinik, salt okunur ve iptal abonelik
/// listelenmez; yanıt asgari veridir (kiracı/klinik kimliği, e-posta yok); "şu an açık" önce; il/ilçe adres biçimi; panel ucu kiracı
/// izolasyonu; en yakın saat ucu anahtarla; hız sınırı politikası. Her test kendi ilini kullanır, paylaşılan veritabanında karışmaz.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class VetFinderIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] AdminPermissions = [PermissionCatalog.Clinics.Read, PermissionCatalog.Clinics.Update];

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public VetFinderIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private sealed record Ctx(string Token, Guid TenantId, Guid ClinicId, string Url);

    /// <summary>Benzersiz il: "Testil Çğ{rastgele}" → adres biçimi "testil-cg{rastgele}".</summary>
    private static string NewCity() => $"Testil Çğ{Guid.NewGuid():N}"[..20];

    private async Task<Ctx> SeedAsync(string city, string district = "Kadıköy", string name = "Patiler Veteriner", bool openAllDay = false)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = DateTime.UtcNow;

        var tenant = new Tenant($"Vb-{Guid.NewGuid():N}"[..16], now);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, now, trialDays: 14));
        var clinic = new Clinic(tenant.Id, name, city, "05550000000", "gizli@example.com", "Moda Cad. 1", "iç açıklama", district);
        db.Clinics.Add(clinic);
        if (openAllDay)
        {
            foreach (var day in Enum.GetValues<DayOfWeek>())
                db.ClinicWorkingHours.Add(ClinicWorkingHour.Create(tenant.Id, clinic.Id, day, false, new TimeOnly(0, 0), new TimeOnly(23, 59), null, null, now));
        }

        await db.SaveChangesAsync();
        var token = await IntegrationTestAuthHelper.SeedTenantWideAdminAndIssueTokenAsync(db, jwt, hasher, tenant.Id, AdminPermissions);
        return new Ctx(token, tenant.Id, clinic.Id, $"/api/v1/clinics/{clinic.Id}/vet-finder");
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

    private static async Task<JsonElement> OkJsonAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<JsonElement> ListAsync(Ctx ctx, bool listed = true, string[]? services = null)
        => await OkJsonAsync(await SendAsync(HttpMethod.Put, ctx.Url, ctx.Token, new { isListed = listed, services = services ?? ["asi"] }));

    private Task<HttpResponseMessage> PublicListAsync(string city, string? query = null)
        => _client.GetAsync($"/api/v1/public/vet-finder/clinics?city={Uri.EscapeDataString(TurkishSlug.From(city))}{query}");

    private async Task<List<string>> PublicNamesAsync(string city, string? query = null)
    {
        var response = await PublicListAsync(city, query);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return [];
        return (await OkJsonAsync(response)).GetProperty("clinics").EnumerateArray().Select(c => c.GetProperty("name").GetString()!).ToList();
    }

    private async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    [Fact]
    public async Task Listing_Should_Be_Opt_In_And_Leave_Immediately_When_Turned_Off()
    {
        var city = NewCity();
        var ctx = await SeedAsync(city);

        var initial = await OkJsonAsync(await SendAsync(HttpMethod.Get, ctx.Url, ctx.Token));
        initial.GetProperty("isListed").GetBoolean().Should().BeFalse("varsayılan kapalı (K4)");
        initial.GetProperty("publicPath").GetString().Should().Be($"/veteriner/{TurkishSlug.From(city)}/kadikoy");
        (await PublicListAsync(city)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var listed = await ListAsync(ctx);
        listed.GetProperty("isListed").GetBoolean().Should().BeTrue();
        listed.GetProperty("services").EnumerateArray().Select(s => s.GetString()).Should().Equal("asi");
        (await PublicNamesAsync(city)).Should().Equal("Patiler Veteriner");

        await ListAsync(ctx, listed: false);
        (await PublicListAsync(city)).StatusCode.Should().Be(HttpStatusCode.NotFound, "kapatınca hemen çıkar");
    }

    [Theory]
    [InlineData("inactiveClinic")]
    [InlineData("readOnly")]
    [InlineData("cancelled")]
    [InlineData("inactiveTenant")]
    public async Task Closed_Clinic_Or_Closed_Subscription_Should_Not_Be_Listed(string closure)
    {
        var city = NewCity();
        var ctx = await SeedAsync(city);
        await ListAsync(ctx);
        (await PublicNamesAsync(city)).Should().ContainSingle();

        await WithDbAsync(db => closure switch
        {
            "inactiveClinic" => db.Clinics.Where(c => c.Id == ctx.ClinicId).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false)),
            "readOnly" => db.TenantSubscriptions.Where(s => s.TenantId == ctx.TenantId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.TrialEndsAtUtc, DateTime.UtcNow.AddMinutes(-1))),
            "cancelled" => db.TenantSubscriptions.Where(s => s.TenantId == ctx.TenantId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, TenantSubscriptionStatus.Cancelled)),
            _ => db.Tenants.Where(t => t.Id == ctx.TenantId).ExecuteUpdateAsync(s => s.SetProperty(t => t.IsActive, false)),
        });

        (await PublicNamesAsync(city)).Should().BeEmpty();
        var regions = await OkJsonAsync(await _client.GetAsync("/api/v1/public/vet-finder/regions"));
        regions.GetProperty("cities").EnumerateArray().Select(c => c.GetProperty("slug").GetString())
            .Should().NotContain(TurkishSlug.From(city));
    }

    [Fact]
    public async Task Public_Response_Should_Carry_Minimal_Data_And_No_Internal_Ids()
    {
        var city = NewCity();
        var ctx = await SeedAsync(city);
        await ListAsync(ctx, services: ["mikrocip", "asi"]);

        var body = await (await PublicListAsync(city)).Content.ReadAsStringAsync();
        body.Should().NotContain(ctx.TenantId.ToString()).And.NotContain(ctx.ClinicId.ToString())
            .And.NotContain("gizli@example.com").And.NotContain("iç açıklama");

        var list = JsonDocument.Parse(body).RootElement;
        var card = list.GetProperty("clinics").EnumerateArray().Single();
        card.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "key", "name", "city", "citySlug", "district", "districtSlug", "address", "phone", "services", "workingHours", "isOpenNow",
            "distanceKm", "sitePath", "bookingUrl", "showsNextSlot");
        card.GetProperty("key").GetString().Should().HaveLength(10);
        card.GetProperty("services").EnumerateArray().Select(s => s.GetProperty("key").GetString()).Should().Equal("asi", "mikrocip");
        card.GetProperty("sitePath").ValueKind.Should().Be(JsonValueKind.Null, "yayında site yok; kart temel bilgiyle çalışır");
        card.GetProperty("bookingUrl").ValueKind.Should().Be(JsonValueKind.Null);

        var regions = await OkJsonAsync(await _client.GetAsync("/api/v1/public/vet-finder/regions"));
        var place = regions.GetProperty("cities").EnumerateArray().Single(c => c.GetProperty("slug").GetString() == TurkishSlug.From(city));
        place.GetProperty("name").GetString().Should().Be(city);
        var district = place.GetProperty("districts").EnumerateArray().Single();
        district.GetProperty("slug").GetString().Should().Be("kadikoy");
        district.GetProperty("name").GetString().Should().Be("Kadıköy");
        regions.GetProperty("minClinicsToIndex").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task Open_Clinics_Should_Come_First_And_Filters_Should_Apply()
    {
        var city = NewCity();
        var closed = await SeedAsync(city, name: "Ada Veteriner");
        var open = await SeedAsync(city, name: "Zeytin Veteriner", openAllDay: true);
        var other = await SeedAsync(city, district: "Üsküdar", name: "Bora Veteriner");
        await ListAsync(closed);
        await ListAsync(open, services: ["cerrahi"]);
        await ListAsync(other);

        // 23:59'dan sonra (İstanbul) açık klinik de kapanır; sıra o dakikada ad sırasına döner.
        if (TimeOnly.FromDateTime(Backend.Veteriner.Application.Common.Time.OperationDayBounds.ToLocal(DateTime.UtcNow)) < new TimeOnly(23, 58))
        {
            (await PublicNamesAsync(city)).Should().Equal("Zeytin Veteriner", "Ada Veteriner", "Bora Veteriner");
            (await PublicNamesAsync(city, "&openNow=true")).Should().Equal("Zeytin Veteriner");
        }

        (await PublicNamesAsync(city, "&district=uskudar")).Should().Equal("Bora Veteriner");
        (await PublicNamesAsync(city, "&district=kadikoy&service=asi")).Should().Equal("Ada Veteriner");
        (await PublicListAsync(city, "&district=sisli")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PublicListAsync(city, "&service=bilinmeyen")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var empty = await OkJsonAsync(await PublicListAsync(city, "&district=uskudar&service=cerrahi"));
        empty.GetProperty("total").GetInt32().Should().Be(0);
        empty.GetProperty("suggestedDistricts").EnumerateArray().Select(d => d.GetProperty("slug").GetString()).Should().Equal("kadikoy");
        empty.GetProperty("indexable").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Panel_Endpoint_Should_Not_Reach_Another_Tenants_Clinic()
    {
        var mine = await SeedAsync(NewCity());
        var theirs = await SeedAsync(NewCity());

        var read = await SendAsync(HttpMethod.Get, theirs.Url, mine.Token);
        read.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var write = await SendAsync(HttpMethod.Put, theirs.Url, mine.Token, new { isListed = true, services = Array.Empty<string>() });
        write.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await OkJsonAsync(await SendAsync(HttpMethod.Get, theirs.Url, theirs.Token))).GetProperty("isListed").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Panel_Should_Validate_Services_And_Location()
    {
        var ctx = await SeedAsync(NewCity());

        (await SendAsync(HttpMethod.Put, ctx.Url, ctx.Token, new { isListed = true, services = new[] { "en-iyi" } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Put, ctx.Url, ctx.Token, new { isListed = true, services = Array.Empty<string>(), latitude = 48.85, longitude = 2.35 }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var saved = await OkJsonAsync(await SendAsync(HttpMethod.Put, ctx.Url, ctx.Token,
            new { isListed = true, services = Array.Empty<string>(), latitude = 40.990123, longitude = 29.028765 }));
        saved.GetProperty("latitude").GetDecimal().Should().Be(40.990123m);
    }

    [Fact]
    public async Task Visitor_Location_Should_Order_By_Distance_Without_Echoing_It()
    {
        var city = NewCity();
        var near = await SeedAsync(city, name: "Yakın Veteriner");
        var far = await SeedAsync(city, name: "Ada Uzak Veteriner");
        await SendAsync(HttpMethod.Put, near.Url, near.Token, new { isListed = true, services = Array.Empty<string>(), latitude = 41.04, longitude = 29.01 });
        await SendAsync(HttpMethod.Put, far.Url, far.Token, new { isListed = true, services = Array.Empty<string>(), latitude = 39.92, longitude = 32.85 });

        (await PublicNamesAsync(city)).Should().Equal("Ada Uzak Veteriner", "Yakın Veteriner");
        var body = await OkJsonAsync(await PublicListAsync(city, "&lat=41.0512&lng=29.0034"));
        body.GetProperty("clinics").EnumerateArray().Select(c => c.GetProperty("name").GetString()).Should().Equal("Yakın Veteriner", "Ada Uzak Veteriner");
        body.GetProperty("usesDistance").GetBoolean().Should().BeTrue();
        body.GetRawText().Should().NotContain("41.0512").And.NotContain("41.04,");
    }

    [Fact]
    public async Task Next_Slots_Should_Answer_By_Key_And_Hide_Unlisted()
    {
        var city = NewCity();
        await ListAsync(await SeedAsync(city));
        var key = (await OkJsonAsync(await PublicListAsync(city))).GetProperty("clinics")[0].GetProperty("key").GetString()!;

        var slots = await OkJsonAsync(await _client.GetAsync($"/api/v1/public/vet-finder/next-slots?keys={key},bilinmeyen"));
        slots.GetProperty("items").EnumerateArray().Select(i => (i.GetProperty("key").GetString(), i.GetProperty("slot").ValueKind))
            .Should().Equal((key, JsonValueKind.Null), ("bilinmeyen", JsonValueKind.Null));

        var tooMany = string.Join(',', Enumerable.Range(0, 25).Select(i => $"k{i:D9}"));
        (await _client.GetAsync($"/api/v1/public/vet-finder/next-slots?keys={tooMany}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void Public_Endpoints_Should_Use_Public_Site_Rate_Limit()
        => typeof(PublicVetFinderController).GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName
            .Should().Be(RateLimitingExtensions.PublicSitePolicy);
}
