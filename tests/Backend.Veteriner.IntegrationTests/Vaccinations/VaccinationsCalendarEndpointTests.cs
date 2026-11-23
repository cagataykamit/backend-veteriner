using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Vaccinations;

/// <summary>Takvim aşı katmanı ucu (APPOINTMENT_CALENDAR_API_CONTRACT.md §5): kaynaklar, durumlar, klinik kapsamı ve yetki.</summary>
[Collection("products-api")]
public sealed class VaccinationsCalendarEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public VaccinationsCalendarEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task GetCalendar_Returns_Planned_NextDue_And_Applied_Of_Clinic()
    {
        var ctx = await SeedAsync(PermissionCatalog.Vaccinations.Read);
        var http = Client(ctx.Token);

        var response = await http.GetAsync(Url(ctx));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();

        var keys = items.Select(i => (i.GetProperty("vaccinationId").GetGuid(), i.GetProperty("kind").GetString(), i.GetProperty("state").GetString())).ToList();
        keys.Should().BeEquivalentTo(new[]
        {
            (ctx.PlannedTomorrowId, "Planned", "Upcoming"),
            (ctx.ImportedNextDueTodayId, "NextDue", "DueToday"),
            (ctx.AppliedTodayId, "Applied", "Completed"),
        });

        var imported = items.Single(i => i.GetProperty("vaccinationId").GetGuid() == ctx.ImportedNextDueTodayId);
        imported.GetProperty("date").GetString().Should().Be(ctx.Today.ToString("yyyy-MM-dd"));
        imported.GetProperty("petName").GetString().Should().Be("Pamuk");
        imported.GetProperty("clientName").GetString().Should().Be("Ayşe Yılmaz");
        imported.GetProperty("vaccineName").GetString().Should().Be("Karma");
    }

    [Fact]
    public async Task GetCalendar_Marks_PastDue_In_Window_As_Overdue()
    {
        var ctx = await SeedAsync(PermissionCatalog.Vaccinations.Read);
        var http = Client(ctx.Token);
        var from = OperationDayBounds.ForLocalDate(ctx.Today.AddDays(-7)).DayStartUtc;
        var to = OperationDayBounds.ForLocalDate(ctx.Today).DayStartUtc;

        var items = await http.GetFromJsonAsync<JsonElement>(
            $"/api/v1/vaccinations/calendar?dateFromUtc={from:O}&dateToUtc={to:O}&clinicId={ctx.ClinicId:D}");

        var overdue = items.EnumerateArray().Single();
        overdue.GetProperty("vaccinationId").GetGuid().Should().Be(ctx.OverdueId);
        overdue.GetProperty("state").GetString().Should().Be("Overdue");
    }

    [Fact]
    public async Task GetCalendar_Hides_Due_Older_Than_One_Calendar_Month()
    {
        var ctx = await SeedAsync(PermissionCatalog.Vaccinations.Read);
        var http = Client(ctx.Token);
        var windowStartDay = ctx.Today.AddMonths(-1);
        var from = OperationDayBounds.ForLocalDate(windowStartDay.AddDays(-10)).DayStartUtc;
        var to = OperationDayBounds.ForLocalDate(windowStartDay.AddDays(1)).DayStartUtc;
        var query = $"/api/v1/vaccinations/calendar?dateFromUtc={from:O}&dateToUtc={to:O}&clinicId={ctx.ClinicId:D}";

        var items = await http.GetFromJsonAsync<JsonElement>(query);
        var summary = await http.GetFromJsonAsync<JsonElement>(query.Replace("/calendar?", "/calendar/summary?"));

        var only = items.EnumerateArray().Single();
        only.GetProperty("vaccinationId").GetGuid().Should().Be(ctx.WindowStartNextDueId);
        only.GetProperty("state").GetString().Should().Be("Overdue");
        summary.EnumerateArray().Sum(d => d.GetProperty("total").GetInt32()).Should().Be(1);
    }

    [Fact]
    public async Task GetCalendar_Without_VaccinationsRead_Is_Forbidden()
    {
        var ctx = await SeedAsync(PermissionCatalog.Appointments.Read);

        var response = await Client(ctx.Token).GetAsync(Url(ctx));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetCalendar_Window_Over_45_Days_Is_BadRequest()
    {
        var ctx = await SeedAsync(PermissionCatalog.Vaccinations.Read);
        var from = OperationDayBounds.ForLocalDate(ctx.Today).DayStartUtc;

        var response = await Client(ctx.Token).GetAsync(
            $"/api/v1/vaccinations/calendar?dateFromUtc={from:O}&dateToUtc={from.AddDays(46):O}&clinicId={ctx.ClinicId:D}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private HttpClient Client(string token)
    {
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static string Url(SeedCtx ctx)
    {
        var from = OperationDayBounds.ForLocalDate(ctx.Today).DayStartUtc;
        var to = OperationDayBounds.ForLocalDate(ctx.Today.AddDays(7)).DayStartUtc;
        return $"/api/v1/vaccinations/calendar?dateFromUtc={from:O}&dateToUtc={to:O}&clinicId={ctx.ClinicId:D}";
    }

    private sealed record SeedCtx(
        string Token,
        Guid ClinicId,
        DateOnly Today,
        Guid PlannedTomorrowId,
        Guid ImportedNextDueTodayId,
        Guid AppliedTodayId,
        Guid OverdueId,
        Guid WindowStartNextDueId);

    private async Task<SeedCtx> SeedAsync(string permission)
    {
        using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"VacCal-{Guid.NewGuid():N}"[..16], DateTime.UtcNow);
        var clinic = new Clinic(tenant.Id, "K1", "Istanbul");
        var otherClinic = new Clinic(tenant.Id, "K2", "Istanbul");
        var owner = new Client(tenant.Id, "Ayşe Yılmaz", DateTime.UtcNow, "905551112201", "takvim-asi@example.com");
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(tenant.Id, owner.Id, "Pamuk", speciesId);
        var definitions = await db.VaccineDefinitions.AsNoTracking()
            .Where(v => v.TenantId == null && v.IsActive)
            .OrderBy(v => v.Code)
            .Select(v => v.Id)
            .Take(4)
            .ToListAsync();
        var (karma, kuduz, parazit, lepto) = (definitions[0], definitions[1], definitions[2], definitions[3]);

        var now = DateTime.UtcNow;
        var today = OperationDayBounds.ToLocalDate(now);
        var todayNoonUtc = OperationDayBounds.NoonUtcForLocalDate(today);
        var importBatch = ImportBatch.Create(tenant.Id, clinic.Id, Guid.NewGuid(), "E-vet", Guid.NewGuid(), now).Value!;

        Vaccination Make(Guid definitionId, string name, VaccinationStatus status, DateTime? applied, DateTime? due, Guid? clinicId = null)
            => new(tenant.Id, pet.Id, clinicId ?? clinic.Id, null, definitionId, name, status, applied, due, null, now);

        var plannedTomorrow = Make(kuduz, "Kuduz", VaccinationStatus.Scheduled, null, todayNoonUtc.AddDays(1));
        var importedNextDueToday = Vaccination.CreateImported(tenant.Id, pet.Id, clinic.Id, karma, "Karma", todayNoonUtc.AddYears(-1), todayNoonUtc, null, importBatch.Id, now);
        var appliedToday = Make(parazit, "İç parazit", VaccinationStatus.Applied, OperationDayBounds.ForLocalDate(today).DayStartUtc.AddMinutes(1), null);
        // 1 aylık gecikmiş penceresi (2026-11-21): pencere başı günündeki vade görünür, bir gün öncesi görünmez. Aynı aşının planlı kaydı
        // sonraki tarihi kapatır (2026-11-23); pencere satırları planlısı olmayan aşılarla kurulur.
        var windowStartNextDue = Vaccination.CreateImported(
            tenant.Id, pet.Id, clinic.Id, lepto, "Lepto", todayNoonUtc.AddYears(-2),
            OperationDayBounds.ForLocalDate(today.AddMonths(-1)).DayStartUtc, null, importBatch.Id, now);
        // Görünmemesinin tek nedeni pencere olsun (iç parazitin sonraki tarihi zaten karşılanmış).
        var tooOldPlanned = Make(parazit, "İç parazit", VaccinationStatus.Scheduled, null, OperationDayBounds.NoonUtcForLocalDate(today.AddMonths(-1).AddDays(-1)));
        var overdue = Make(kuduz, "Kuduz", VaccinationStatus.Scheduled, null, todayNoonUtc.AddDays(-3));
        // Karşılanmış sonraki tarih: aynı aşı sonradan yeniden uygulanmış (o uygulama aralık dışında).
        var fulfilledSource = Make(parazit, "İç parazit", VaccinationStatus.Applied, todayNoonUtc.AddMonths(-6), todayNoonUtc.AddDays(2));
        var laterDose = Make(parazit, "İç parazit", VaccinationStatus.Applied, todayNoonUtc.AddDays(-20), null);
        var cancelled = Make(kuduz, "Kuduz", VaccinationStatus.Cancelled, null, todayNoonUtc.AddDays(2));
        var otherClinicPlanned = Make(kuduz, "Kuduz", VaccinationStatus.Scheduled, null, todayNoonUtc.AddDays(2), otherClinic.Id);

        db.AddRange(tenant, clinic, otherClinic, owner, pet, importBatch,
            plannedTomorrow, importedNextDueToday, appliedToday, overdue, fulfilledSource, laterDose, cancelled, otherClinicPlanned,
            windowStartNextDue, tooOldPlanned);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 400));
        await db.SaveChangesAsync();

        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var token = await IntegrationTestAuthHelper.SeedScopedListReaderAndIssueTokenAsync(db, jwt, hasher, tenant.Id, clinic.Id, permission);

        return new SeedCtx(token, clinic.Id, today, plannedTomorrow.Id, importedNextDueToday.Id, appliedToday.Id, overdue.Id, windowStartNextDue.Id);
    }
}
