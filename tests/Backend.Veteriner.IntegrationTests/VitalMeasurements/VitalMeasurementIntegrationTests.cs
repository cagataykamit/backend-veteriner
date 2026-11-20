using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.VitalMeasurements;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.VitalMeasurements;

/// <summary>
/// HOSP-004 vital ölçümü: HTTP yaşam döngüsü, klinik kapsamı (404), izin (403), eşzamanlılık (409), kesinleşmiş muayene geç kaydı
/// ve muayene vitalleriyle birleşen eğilim. Kullanıcı rol bağlarıyla (seed) giriş yapan ClinicAdmin'dir.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class VitalMeasurementIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public VitalMeasurementIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private sealed record Ctx(HttpClient Client, Guid ClinicId, Guid UnassignedClinicId, Guid PetId);

    private async Task<Ctx> ClinicAdminAsync()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (email, password, clinicId, unassignedClinicId) = await IntegrationTestAuthHelper.SeedClinicAdminUserAsync(_factory.Services, hasher);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, clinicId);
        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return new Ctx(client, clinicId, unassignedClinicId, petId);
    }

    private static async Task<Guid> CreateAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/v1/vital-measurements", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, Guid id)
        => await client.GetFromJsonAsync<JsonElement>($"/api/v1/vital-measurements/{id}");

    private async Task<(Guid HospitalizationId, Guid PetId)> SeedStayAsync(Guid clinicId, TimeSpan admittedOffset)
    {
        var hospitalizationId = await IntegrationTestAuthHelper.SeedHospitalizationInClinicAsync(_factory.Services, clinicId, admittedOffset);
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var petId = await db.Hospitalizations.Where(h => h.Id == hospitalizationId).Select(h => h.PetId).SingleAsync();
        return (hospitalizationId, petId);
    }

    [Fact]
    public async Task Measurement_Should_Be_Created_Listed_Updated_Tracked_And_Cancelled()
    {
        var ctx = await ClinicAdminAsync();
        var (stayId, petId) = await SeedStayAsync(ctx.ClinicId, TimeSpan.FromHours(-6));
        var at = DateTime.UtcNow.AddHours(-1);

        var outside = await ctx.Client.PostAsJsonAsync("/api/v1/vital-measurements", new
        {
            ctx.ClinicId, PetId = petId, HospitalizationId = stayId, MeasuredAtUtc = DateTime.UtcNow.AddHours(-7), TemperatureC = 38.6m,
        });
        outside.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(outside)).Should().Be("VitalMeasurements.OutsideHospitalization");

        var id = await CreateAsync(ctx.Client, new
        {
            ctx.ClinicId, PetId = petId, HospitalizationId = stayId, MeasuredAtUtc = at,
            TemperatureC = 38.64m, HeartRateBpm = 120, RespiratoryRatePerMin = 24, WeightKg = 12.456m, Note = " Sakin ",
        });

        var detail = await GetAsync(ctx.Client, id);
        detail.GetProperty("temperatureC").GetDecimal().Should().Be(38.6m);
        detail.GetProperty("weightKg").GetDecimal().Should().Be(12.46m);
        detail.GetProperty("note").GetString().Should().Be("Sakin");
        detail.GetProperty("hospitalizationId").GetGuid().Should().Be(stayId);
        detail.GetProperty("petName").GetString().Should().NotBeEmpty();
        var version = detail.GetProperty("rowVersion").GetString();

        var put = await ctx.Client.PutAsJsonAsync($"/api/v1/vital-measurements/{id}", new
        {
            MeasuredAtUtc = at, TemperatureC = 39.1m, HeartRateBpm = 130, RowVersion = version,
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        var newVersion = (await put.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rowVersion").GetString();
        newVersion.Should().NotBe(version);

        var stale = await ctx.Client.PutAsJsonAsync($"/api/v1/vital-measurements/{id}", new
        {
            MeasuredAtUtc = at, TemperatureC = 40m, RowVersion = version,
        });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(stale)).Should().Be("VitalMeasurements.ConcurrencyConflict");

        var afterPut = await GetAsync(ctx.Client, id);
        afterPut.GetProperty("temperatureC").GetDecimal().Should().Be(39.1m);
        afterPut.GetProperty("weightKg").ValueKind.Should().Be(JsonValueKind.Null);
        afterPut.GetProperty("note").ValueKind.Should().Be(JsonValueKind.Null);

        var list = await ctx.Client.GetFromJsonAsync<JsonElement>($"/api/v1/vital-measurements?petId={petId:D}&hospitalizationId={stayId:D}");
        list.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid().Should().Be(id);

        var history = await ctx.Client.GetFromJsonAsync<JsonElement>($"/api/v1/vital-measurements/{id}/history");
        history.EnumerateArray().Select(r => r.GetProperty("kind").GetString()).Should().Equal("Updated", "Created");
        history[0].GetProperty("changes").EnumerateArray().Select(c => c.GetProperty("field").GetString())
            .Should().Contain(["TemperatureC", "HeartRateBpm", "RespiratoryRatePerMin", "WeightKg", "Note"]);

        var timeline = await ctx.Client.GetFromJsonAsync<JsonElement>($"/api/v1/pets/{petId:D}/timeline?types=VitalMeasurement");
        timeline.GetProperty("items").EnumerateArray().Single().GetProperty("summary").GetString()
            .Should().Be("39,1 °C · Nabız 130/dk");

        var shortReason = await ctx.Client.PostAsJsonAsync($"/api/v1/vital-measurements/{id}/cancel", new { reason = "x", rowVersion = newVersion });
        shortReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var cancel = await ctx.Client.PostAsJsonAsync($"/api/v1/vital-measurements/{id}/cancel", new { reason = "Yanlış hastaya girildi", rowVersion = newVersion });
        cancel.StatusCode.Should().Be(HttpStatusCode.OK, await cancel.Content.ReadAsStringAsync());
        var cancelledVersion = (await cancel.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rowVersion").GetString();

        var again = await ctx.Client.PutAsJsonAsync($"/api/v1/vital-measurements/{id}", new { MeasuredAtUtc = at, TemperatureC = 38m, RowVersion = cancelledVersion });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(again)).Should().Be("VitalMeasurements.AlreadyCancelled");

        (await ctx.Client.GetFromJsonAsync<JsonElement>($"/api/v1/vital-measurements?petId={petId:D}"))
            .GetProperty("items").GetArrayLength().Should().Be(0);
        (await ctx.Client.GetFromJsonAsync<JsonElement>($"/api/v1/vital-measurements?petId={petId:D}&includeCancelled=true"))
            .GetProperty("items").EnumerateArray().Single().GetProperty("isCancelled").GetBoolean().Should().BeTrue();
        (await ctx.Client.GetFromJsonAsync<JsonElement>($"/api/v1/pets/{petId:D}/timeline?types=VitalMeasurement"))
            .GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Other_Clinic_Measurement_Should_Be_NotFound_And_Create_There_Forbidden()
    {
        var ctx = await ClinicAdminAsync();
        Guid hiddenId;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenantId = await db.Clinics.Where(c => c.Id == ctx.UnassignedClinicId).Select(c => c.TenantId).SingleAsync();
            var hidden = VitalMeasurement.Create(
                tenantId, ctx.UnassignedClinicId, ctx.PetId, null, null, DateTime.UtcNow.AddHours(-1),
                VitalSigns.Create(38m, null, null, null).Value!, null, Guid.NewGuid(), DateTime.UtcNow).Value!;
            db.VitalMeasurements.Add(hidden);
            await db.SaveChangesAsync();
            hiddenId = hidden.Id;
        }

        (await ctx.Client.GetAsync($"/api/v1/vital-measurements/{hiddenId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var version = Convert.ToBase64String(new byte[8]);
        (await ctx.Client.PutAsJsonAsync($"/api/v1/vital-measurements/{hiddenId}", new { MeasuredAtUtc = DateTime.UtcNow, TemperatureC = 38m, RowVersion = version }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ctx.Client.PostAsJsonAsync($"/api/v1/vital-measurements/{hiddenId}/cancel", new { reason = "Yanlış hastaya girildi", rowVersion = version }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var list = await ctx.Client.GetFromJsonAsync<JsonElement>($"/api/v1/vital-measurements?petId={ctx.PetId:D}");
        list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().NotContain(hiddenId);

        var create = await ctx.Client.PostAsJsonAsync("/api/v1/vital-measurements", new
        {
            ClinicId = ctx.UnassignedClinicId, ctx.PetId, MeasuredAtUtc = DateTime.UtcNow.AddMinutes(-5), TemperatureC = 38m,
        });
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Missing_Permissions_Should_Return_403()
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (email, _, clinicId, _) = await IntegrationTestAuthHelper.SeedClinicAdminUserAsync(_factory.Services, hasher);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, clinicId);

        var readOnly = _factory.CreateClient();
        readOnly.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, [PermissionCatalog.VitalMeasurements.Read]));
        var create = await readOnly.PostAsJsonAsync("/api/v1/vital-measurements", new
        {
            ClinicId = clinicId, PetId = petId, MeasuredAtUtc = DateTime.UtcNow.AddMinutes(-5), TemperatureC = 38m,
        });
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var noRead = _factory.CreateClient();
        noRead.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, [PermissionCatalog.Pets.Read]));
        (await noRead.GetAsync($"/api/v1/vital-measurements?petId={petId:D}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await noRead.GetAsync($"/api/v1/vital-measurements/series?petId={petId:D}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Finalized_Examination_Should_Require_Late_Entry_Reason_And_Lock_Update()
    {
        var ctx = await ClinicAdminAsync();
        var seed = await IntegrationTestAuthHelper.SeedExaminationInClinicAsync(_factory.Services, ctx.ClinicId);
        Guid petId;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var exam = db.Examinations.Single(e => e.Id == seed.ExaminationId);
            exam.Finalize(Guid.NewGuid(), DateTime.UtcNow).IsSuccess.Should().BeTrue();
            await db.SaveChangesAsync();
            petId = exam.PetId;
        }

        object Body(string? reason) => new
        {
            ctx.ClinicId, PetId = petId, ExaminationId = seed.ExaminationId, MeasuredAtUtc = DateTime.UtcNow.AddMinutes(-10),
            WeightKg = 4.2m, LateEntryReason = reason,
        };

        var withoutReason = await ctx.Client.PostAsJsonAsync("/api/v1/vital-measurements", Body(null));
        withoutReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(withoutReason)).Should().Be("VitalMeasurements.LateEntryReasonRequired");

        var id = await CreateAsync(ctx.Client, Body("Kesinleştirmeden sonra tartıldı"));
        var detail = await GetAsync(ctx.Client, id);
        detail.GetProperty("addedAfterFinalization").GetBoolean().Should().BeTrue();

        var put = await ctx.Client.PutAsJsonAsync($"/api/v1/vital-measurements/{id}", new
        {
            MeasuredAtUtc = DateTime.UtcNow.AddMinutes(-10), WeightKg = 4.3m, RowVersion = detail.GetProperty("rowVersion").GetString(),
        });
        put.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(put)).Should().Be("Examinations.Finalized");
    }

    [Fact]
    public async Task Series_Should_Merge_Examination_Weight_With_Measurements_And_Respect_Stay_Window()
    {
        var ctx = await ClinicAdminAsync();
        var (stayId, petId) = await SeedStayAsync(ctx.ClinicId, TimeSpan.FromHours(-6));
        Guid examId;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenantId = await db.Clinics.Where(c => c.Id == ctx.ClinicId).Select(c => c.TenantId).SingleAsync();
            var inStay = new Examination(
                tenantId, ctx.ClinicId, petId, null, DateTime.UtcNow.AddHours(-5), "Kabul muayenesi", "Bulgu", null, null, DateTime.UtcNow,
                weightKg: 12.6m, temperatureC: 39.4m);
            var beforeStay = new Examination(
                tenantId, ctx.ClinicId, petId, null, DateTime.UtcNow.AddDays(-3), "Önceki kontrol", "Bulgu", null, null, DateTime.UtcNow,
                weightKg: 13.1m);
            var withoutVitals = new Examination(
                tenantId, ctx.ClinicId, petId, null, DateTime.UtcNow.AddHours(-4), "Vitalsiz", "Bulgu", null, null, DateTime.UtcNow);
            db.Examinations.AddRange(inStay, beforeStay, withoutVitals);
            await db.SaveChangesAsync();
            examId = inStay.Id;
        }

        var measurementId = await CreateAsync(ctx.Client, new
        {
            ctx.ClinicId, PetId = petId, HospitalizationId = stayId, MeasuredAtUtc = DateTime.UtcNow.AddHours(-1), TemperatureC = 38.6m, WeightKg = 12.4m,
        });

        var all = await ctx.Client.GetFromJsonAsync<JsonElement>($"/api/v1/vital-measurements/series?petId={petId:D}");
        var points = all.GetProperty("points").EnumerateArray().ToList();
        points.Select(p => p.GetProperty("weightKg").GetDecimal()).Should().Equal(13.1m, 12.6m, 12.4m);
        points.Select(p => p.GetProperty("source").GetString()).Should().Equal("Examination", "Examination", "VitalMeasurement");
        points[1].GetProperty("sourceId").GetGuid().Should().Be(examId);
        points[1].GetProperty("examinationId").GetGuid().Should().Be(examId);
        points[2].GetProperty("sourceId").GetGuid().Should().Be(measurementId);
        points[2].GetProperty("hospitalizationId").GetGuid().Should().Be(stayId);
        all.GetProperty("truncated").GetBoolean().Should().BeFalse();
        all.GetProperty("omittedSources").GetArrayLength().Should().Be(0);

        var stay = await ctx.Client.GetFromJsonAsync<JsonElement>($"/api/v1/vital-measurements/series?petId={petId:D}&hospitalizationId={stayId:D}");
        stay.GetProperty("points").EnumerateArray().Select(p => p.GetProperty("weightKg").GetDecimal()).Should().Equal(12.6m, 12.4m);

        var limited = await ctx.Client.GetFromJsonAsync<JsonElement>($"/api/v1/vital-measurements/series?petId={petId:D}&limit=1");
        limited.GetProperty("truncated").GetBoolean().Should().BeTrue();
        limited.GetProperty("points").EnumerateArray().Single().GetProperty("sourceId").GetGuid().Should().Be(measurementId);
    }
}
