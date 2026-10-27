using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Treatments;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Operational;

/// <summary>
/// Klinik listelerde <c>sort</c>/<c>order</c> beyaz listesi (SQL Server üzerinde) ve aşı listesi birleşik tarih aralığı.
/// </summary>
[Collection("products-api")]
public sealed class ClinicalListSortAndDateEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ClinicalListSortAndDateEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    public static TheoryData<string, string, string> SortableLists => new()
    {
        { "examinations", PermissionCatalog.Examinations.Read, "examinedAtUtc" },
        { "examinations", PermissionCatalog.Examinations.Read, "ExaminationDateUtc" },
        { "treatments", PermissionCatalog.Treatments.Read, "treatmentDateUtc" },
        { "prescriptions", PermissionCatalog.Prescriptions.Read, "prescribedAtUtc" },
        { "lab-results", PermissionCatalog.LabResults.Read, "requestedAtUtc" },
        { "hospitalizations", PermissionCatalog.Hospitalizations.Read, "admittedAtUtc" },
    };

    [Theory]
    [MemberData(nameof(SortableLists))]
    public async Task Sort_Asc_And_Desc_Should_Be_Applied(string list, string permission, string sort)
    {
        var ctx = await SeedAsync(permission);
        var http = CreateClient(ctx.Token);
        var baseUrl = $"/api/v1/{list}?page=1&pageSize=50&clinicId={ctx.ClinicId:D}";

        var asc = await GetIdsAsync(http, $"{baseUrl}&sort={sort}&order=asc");
        var desc = await GetIdsAsync(http, $"{baseUrl}&sort={sort}&order=desc");
        var byDefault = await GetIdsAsync(http, baseUrl);

        asc.Should().Equal(ctx.OlderFirst[list]);
        desc.Should().Equal(ctx.OlderFirst[list].AsEnumerable().Reverse());
        byDefault.Should().Equal(desc, "sort verilmezse bugünkü varsayılan (en yeni üstte) korunur");
    }

    [Theory]
    [InlineData("examinations")]
    [InlineData("vaccinations")]
    [InlineData("treatments")]
    [InlineData("prescriptions")]
    [InlineData("lab-results")]
    [InlineData("hospitalizations")]
    [InlineData("appointments")]
    public async Task Unknown_Sort_Should_Return_400(string list)
    {
        var permission = list switch
        {
            "examinations" => PermissionCatalog.Examinations.Read,
            "vaccinations" => PermissionCatalog.Vaccinations.Read,
            "treatments" => PermissionCatalog.Treatments.Read,
            "prescriptions" => PermissionCatalog.Prescriptions.Read,
            "lab-results" => PermissionCatalog.LabResults.Read,
            "hospitalizations" => PermissionCatalog.Hospitalizations.Read,
            _ => PermissionCatalog.Appointments.Read,
        };
        var ctx = await SeedAsync(permission);
        var http = CreateClient(ctx.Token);

        var response = await http.GetAsync($"/api/v1/{list}?page=1&pageSize=50&clinicId={ctx.ClinicId:D}&sort=notes&order=asc");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Vaccinations_DateRange_Should_Use_Applied_Or_Due_Date_Inclusive()
    {
        var ctx = await SeedAsync(PermissionCatalog.Vaccinations.Read);
        var http = CreateClient(ctx.Token);
        var from = ctx.RangeFromUtc.ToString("O");
        var to = ctx.RangeToUtc.ToString("O");

        var ids = await GetIdsAsync(
            http,
            $"/api/v1/vaccinations?page=1&pageSize=50&clinicId={ctx.ClinicId:D}&dateFromUtc={Uri.EscapeDataString(from)}&dateToUtc={Uri.EscapeDataString(to)}");

        ids.Should().BeEquivalentTo(ctx.VaccinationsInRange);
    }

    [Theory]
    [InlineData("dueAtUtc")]
    [InlineData("NextDueAtUtc")]
    public async Task Vaccinations_Due_Sort_Should_Order_By_Due_With_Missing_Due_Last(string sort)
    {
        var ctx = await SeedAsync(PermissionCatalog.Vaccinations.Read);
        var http = CreateClient(ctx.Token);
        var baseUrl = $"/api/v1/vaccinations?page=1&pageSize=50&clinicId={ctx.ClinicId:D}";

        var asc = await GetIdsAsync(http, $"{baseUrl}&sort={sort}&order=asc");
        var desc = await GetIdsAsync(http, $"{baseUrl}&sort={sort}&order=desc");
        var appliedOnlyAsc = await GetIdsAsync(http, $"{baseUrl}&sort={sort}&order=asc&status=1");
        var byDefault = await GetIdsAsync(http, baseUrl);

        asc.Should().Equal(ctx.VaccinationsDueAsc);
        desc.Should().Equal(ctx.VaccinationsDueDesc);
        appliedOnlyAsc.Should().Equal(ctx.AppliedDueAscNullLast);
        byDefault.Should().Equal(ctx.VaccinationsDefaultOrder, "sort verilmezse birleşik tarih azalan sıra korunur");
    }

    private HttpClient CreateClient(string token)
    {
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static async Task<List<Guid>> GetIdsAsync(HttpClient http, string url)
    {
        var response = await http.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, url);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
    }

    private sealed record SeedCtx(
        string Token,
        Guid ClinicId,
        Dictionary<string, Guid[]> OlderFirst,
        DateTime RangeFromUtc,
        DateTime RangeToUtc,
        Guid[] VaccinationsInRange,
        Guid[] AppliedDueAscNullLast,
        Guid[] VaccinationsDueAsc,
        Guid[] VaccinationsDueDesc,
        Guid[] VaccinationsDefaultOrder);

    private async Task<SeedCtx> SeedAsync(string permission)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenant = new Tenant($"Sort-{suffix}", DateTime.UtcNow);
        var clinic = new Clinic(tenant.Id, "K1", "Istanbul");
        var client = new Client(tenant.Id, $"Musteri {suffix}", DateTime.UtcNow, "905551112203");
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet1 = new Pet(tenant.Id, client.Id, $"Pet1{suffix}", speciesId);
        var pet2 = new Pet(tenant.Id, client.Id, $"Pet2{suffix}", speciesId);
        var vaccineDefinitionId = await db.VaccineDefinitions.AsNoTracking()
            .Where(v => v.TenantId == null && v.Code == "RABIES")
            .Select(v => v.Id)
            .FirstAsync();

        // Saniye altı kesinlik farkına takılmamak için tam saniyeli zamanlar.
        var now = DateTime.UtcNow;
        var baseTime = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
        var older = baseTime.AddDays(-3);
        var newer = baseTime.AddDays(-1);

        var exams = new[]
        {
            new Examination(tenant.Id, clinic.Id, pet1.Id, null, older, "Kontrol", "Bulgu", null, null, DateTime.UtcNow),
            new Examination(tenant.Id, clinic.Id, pet2.Id, null, newer, "Kontrol", "Bulgu", null, null, DateTime.UtcNow),
        };
        var treatments = new[]
        {
            new Treatment(tenant.Id, clinic.Id, pet1.Id, null, older, "Tedavi", "Açıklama", null, null, DateTime.UtcNow),
            new Treatment(tenant.Id, clinic.Id, pet2.Id, null, newer, "Tedavi", "Açıklama", null, null, DateTime.UtcNow),
        };
        var prescriptions = new[]
        {
            new Prescription(tenant.Id, clinic.Id, pet1.Id, null, null, older, "Reçete", "İçerik", null, null, DateTime.UtcNow),
            new Prescription(tenant.Id, clinic.Id, pet2.Id, null, null, newer, "Reçete", "İçerik", null, null, DateTime.UtcNow),
        };
        var labs = new[]
        {
            new LabResult(tenant.Id, clinic.Id, pet1.Id, null, null, null, older, "Hemogram", "Normal", null, null, DateTime.UtcNow),
            new LabResult(tenant.Id, clinic.Id, pet2.Id, null, null, null, newer, "Hemogram", "Normal", null, null, DateTime.UtcNow),
        };
        var hospitalizations = new[]
        {
            new Hospitalization(tenant.Id, clinic.Id, pet1.Id, null, older, null, "Gözlem", null, DateTime.UtcNow),
            new Hospitalization(tenant.Id, clinic.Id, pet2.Id, null, newer, null, "Gözlem", null, DateTime.UtcNow),
        };

        // Aşı: varsayılan sıra birleşik tarih (Applied → AppliedAtUtc, değilse DueAtUtc); sort=dueAtUtc → vade.
        var rangeFrom = baseTime.AddDays(10);
        var rangeTo = baseTime.AddDays(20);
        Vaccination Scheduled(DateTime due) => new(tenant.Id, pet1.Id, clinic.Id, null, vaccineDefinitionId, "Kuduz", VaccinationStatus.Scheduled, null, due, null, DateTime.UtcNow);
        Vaccination Applied(DateTime applied, DateTime? due) => new(tenant.Id, pet1.Id, clinic.Id, null, vaccineDefinitionId, "Kuduz", VaccinationStatus.Applied, applied, due, null, DateTime.UtcNow);
        var vaccOlder = Scheduled(older);
        var vaccNewer = Scheduled(newer);
        var scheduledAtFromEdge = Scheduled(rangeFrom);
        var scheduledAfterRange = Scheduled(rangeTo.AddSeconds(1));
        var appliedInRange = Applied(rangeTo, null);
        var appliedBeforeRangeDueInside = Applied(rangeFrom.AddDays(-1), rangeFrom.AddDays(2));

        var vaccinationsForSort = new[] { vaccOlder, vaccNewer };
        var vaccinationsForRange = new[] { scheduledAtFromEdge, scheduledAfterRange, appliedInRange, appliedBeforeRangeDueInside };

        db.AddRange(tenant, clinic, client, pet1, pet2);
        db.AddRange(exams);
        db.AddRange(treatments);
        db.AddRange(prescriptions);
        db.AddRange(labs);
        db.AddRange(hospitalizations);
        db.AddRange(vaccinationsForSort);
        db.AddRange(vaccinationsForRange);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 400));
        await db.SaveChangesAsync();

        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var token = await IntegrationTestAuthHelper.SeedScopedListReaderAndIssueTokenAsync(
            db, jwt, hasher, tenant.Id, clinic.Id, permission);

        // Tüm tarih değerleri farklı olduğundan Id eşitlik kırıcısına ihtiyaç yok; vadesiz kayıt her iki yönde sonda.
        var allVaccinations = vaccinationsForSort.Concat(vaccinationsForRange).ToArray();
        var withDue = allVaccinations.Where(v => v.DueAtUtc != null).ToArray();
        var withoutDue = allVaccinations.Where(v => v.DueAtUtc == null).Select(v => v.Id).ToArray();
        var dueAsc = withDue.OrderBy(v => v.DueAtUtc).Select(v => v.Id).Concat(withoutDue).ToArray();
        var dueDesc = withDue.OrderByDescending(v => v.DueAtUtc).Select(v => v.Id).Concat(withoutDue).ToArray();
        var defaultOrder = allVaccinations.OrderByDescending(v => v.AppliedAtUtc ?? v.DueAtUtc).Select(v => v.Id).ToArray();

        return new SeedCtx(
            token,
            clinic.Id,
            new Dictionary<string, Guid[]>
            {
                ["examinations"] = exams.Select(x => x.Id).ToArray(),
                ["treatments"] = treatments.Select(x => x.Id).ToArray(),
                ["prescriptions"] = prescriptions.Select(x => x.Id).ToArray(),
                ["lab-results"] = labs.Select(x => x.Id).ToArray(),
                ["hospitalizations"] = hospitalizations.Select(x => x.Id).ToArray(),
            },
            rangeFrom,
            rangeTo,
            [scheduledAtFromEdge.Id, appliedInRange.Id],
            [appliedBeforeRangeDueInside.Id, appliedInRange.Id],
            dueAsc,
            dueDesc,
            defaultOrder);
    }
}
