using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.DailyTransactions;
using Backend.Veteriner.Application.Reports.Performance;
using Backend.Veteriner.Application.Reports.Performance.Breakdowns;
using Backend.Veteriner.Application.Reports.Performance.ClinicsOverview;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Domain.Visits;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Reports;

/// <summary>
/// Patron ekranı (TIER-007; docs/CLINICS_OVERVIEW_API_CONTRACT.md): şube satırı aynı klinikle açılan özete, gün sonuna ve kırılımlardaki
/// gelişe eşittir; toplam erişilebilen şubelerle açılan özete eşittir; atanmamış ve pasif şube listelenmez; aktif klinik bağlamı
/// listeyi daraltmaz; Kurumsal dışında uç kapalıdır.
/// </summary>
public sealed partial class DailyTransactionsReportIntegrationTests
{
    private const string ClinicsOverview = "/api/v1/reports/clinics-overview";

    private static readonly string[] OverviewPermissions =
    [
        PermissionCatalog.Payments.Read,
        PermissionCatalog.Charges.Read,
        PermissionCatalog.Examinations.Read,
        PermissionCatalog.Vaccinations.Read,
        PermissionCatalog.Appointments.Read,
        PermissionCatalog.Visits.Read,
    ];

    private async Task<Guid> SeedClinicAsync(Guid tenantId, bool active, params Guid[] assignedUserIds)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clinic = new Clinic(tenantId, $"Sube-{Guid.NewGuid():N}"[..14], "Izmir");
        if (!active)
            clinic.Deactivate();
        db.Clinics.Add(clinic);
        await db.SaveChangesAsync();
        foreach (var userId in assignedUserIds)
            db.UserClinics.Add(new UserClinic(userId, clinic.Id, DateTime.UtcNow));
        await db.SaveChangesAsync();
        return clinic.Id;
    }

    /// <summary>Ek şubelere de atanmış ClinicAdmin; <paramref name="activeClinicId"/> JWT'de aktif klinik olur.</summary>
    private async Task<HttpClient> NewHttpForBranchUserAsync(Env env, string[] permissions, Guid[] extraClinicIds, Guid? activeClinicId = null)
    {
        string email;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            Guid userId;
            (userId, email) = await Env.SeedUserAsync(db, hasher, env.TenantId, env.ClinicId);
            foreach (var clinicId in extraClinicIds)
                db.UserClinics.Add(new UserClinic(userId, clinicId, DateTime.UtcNow));
            await db.SaveChangesAsync();
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, permissions, activeClinicId);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    [Fact]
    public async Task Clinics_Overview_Should_Equal_Per_Clinic_Summary_And_Daily_Totals()
    {
        var env = await SeedTenantAsync();
        await SetPlanAsync(env.TenantId, SubscriptionPlanCode.Premium);
        var second = await SeedClinicAsync(env.TenantId, active: true, env.UserId);
        var unassigned = await SeedClinicAsync(env.TenantId, active: true);
        var passive = await SeedClinicAsync(env.TenantId, active: false);

        var a = await env.SeedClientAsync("Şube Acar");
        var b = await env.SeedClientAsync("Şube Bal");
        var c = await env.SeedClientAsync("Şube Can");
        var chargeA1 = await env.SeedChargeAsync(a, 500m, EarlierToday(TimeSpan.FromMinutes(40)));
        await env.PayAsync(a, chargeA1, 300m, PaymentMethod.Card, EarlierToday(TimeSpan.FromMinutes(30)));
        await env.SeedChargeAsync(a, 50m, EarlierToday(TimeSpan.FromMinutes(35)), clinicId: second);
        var chargeB2 = await env.SeedChargeAsync(b, 200m, EarlierToday(TimeSpan.FromMinutes(25)), clinicId: second);
        await env.PayAsync(b, chargeB2, 200m, PaymentMethod.Cash, EarlierToday(TimeSpan.FromMinutes(20)), second);
        await env.SeedChargeAsync(c, 999m, EarlierToday(TimeSpan.FromMinutes(15)), clinicId: unassigned);
        var pet = await env.SeedPetAsync(b);
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Visits.Add(new Visit(env.TenantId, second, pet, null, null, env.UserId, EarlierToday(TimeSpan.FromMinutes(26))));
            await db.SaveChangesAsync();
        }

        // Patron ekranı aktif klinik bağlamıyla (JWT clinic_id) çağrılır; karşılaştırma uçları klinik süzgeciyle, bağlamsız.
        var withActiveClinic = await NewHttpForBranchUserAsync(env, OverviewPermissions, [second, passive], env.ClinicId);
        var reports = await NewHttpForBranchUserAsync(env, OverviewPermissions, [second, passive]);
        var range = $"from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}";

        var response = await withActiveClinic.GetAsync($"{ClinicsOverview}?{range}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var overview = (await response.Content.ReadFromJsonAsync<ClinicsOverviewDto>())!;

        overview.Clinics.Select(r => r.ClinicId).Should().BeEquivalentTo([env.ClinicId, second], "atanmamış ve pasif şube listelenmez");
        overview.PreviousFrom.Should().Be(Today.AddDays(-1));
        foreach (var row in overview.Clinics)
        {
            var summary = (await reports.GetFromJsonAsync<PerformanceSummaryDto>($"{Performance}?{range}&clinicId={row.ClinicId}"))!;
            var daily = (await reports.GetFromJsonAsync<DailyTransactionsReportResultDto>($"{Daily}?{range}&clinicId={row.ClinicId}&pageSize=1"))!;
            var breakdowns = (await reports.GetFromJsonAsync<PerformanceBreakdownsDto>($"{Breakdowns}?{range}&clinicId={row.ClinicId}"))!;

            row.Current.NetChargeAmount.Should().Be(summary.Current.Charges!.NetChargeAmount).And.Be(daily.Totals.NetChargeAmount);
            row.Current.NetCollectedAmount.Should().Be(summary.Current.Cash!.Totals.NetAmount);
            row.Current.ExaminationCount.Should().Be(summary.Current.ExaminationCount);
            row.Current.Appointments.Should().Be(summary.Current.Appointments);
            row.Current.VisitCount.Should().Be(breakdowns.Current.Activity!.VisitCount);
            row.Outstanding.Should().Be(summary.Outstanding);
            row.Previous!.NetChargeAmount.Should().Be(summary.Previous!.Charges!.NetChargeAmount);
        }

        var first = overview.Clinics.Single(r => r.ClinicId == env.ClinicId);
        first.Current.Should().Match<ClinicsOverviewFiguresDto>(f => f.NetChargeAmount == 500m && f.NetCollectedAmount == 300m && f.VisitCount == 0);
        first.Outstanding.Should().Be(new PerformanceOutstandingDto(200m, 1));
        var branch = overview.Clinics.Single(r => r.ClinicId == second);
        branch.Current.Should().Match<ClinicsOverviewFiguresDto>(f => f.NetChargeAmount == 250m && f.NetCollectedAmount == 200m && f.VisitCount == 1);
        branch.Outstanding.Should().Be(new PerformanceOutstandingDto(50m, 1));

        // Toplam = erişilebilen şubelerle açılan özet; tutarlar satır toplamı, alacaklı müşteri tekil.
        var all = (await reports.GetFromJsonAsync<PerformanceSummaryDto>($"{Performance}?{range}"))!;
        overview.Total.Current.NetChargeAmount.Should().Be(all.Current.Charges!.NetChargeAmount).And.Be(750m);
        overview.Total.Current.NetCollectedAmount.Should().Be(all.Current.Cash!.Totals.NetAmount).And.Be(500m);
        overview.Total.Current.VisitCount.Should().Be(1);
        overview.Total.Outstanding.Should().Be(all.Outstanding).And.Be(new PerformanceOutstandingDto(250m, 1));
    }

    [Fact]
    public async Task Clinics_Overview_Should_Be_Premium_Only_And_Follow_Report_Rules()
    {
        var env = await SeedTenantAsync();
        await SetPlanAsync(env.TenantId, SubscriptionPlanCode.Pro);
        var http = await env.NewHttpForAnotherUserAsync(OverviewPermissions);
        var range = $"from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}";

        var locked = await http.GetAsync($"{ClinicsOverview}?{range}");
        locked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var json = await locked.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("Subscriptions.FeatureNotInPlan");
        json.GetProperty("requiredPlanCode").GetString().Should().Be("Premium");

        await SetPlanAsync(env.TenantId, SubscriptionPlanCode.Premium);
        var client = await env.SeedClientAsync("Tek Şube");
        await env.SeedChargeAsync(client, 120m, EarlierToday(TimeSpan.FromMinutes(5)));

        var single = (await http.GetFromJsonAsync<ClinicsOverviewDto>($"{ClinicsOverview}?{range}&compare=false"))!;
        single.Clinics.Should().ContainSingle().Which.ClinicId.Should().Be(env.ClinicId);
        single.Clinics[0].Current.Should().Be(single.Total.Current, "tek şubede toplam satırın aynısıdır");
        single.Clinics[0].Previous.Should().BeNull();
        single.PreviousFrom.Should().BeNull();

        var paymentsOnly = await env.NewHttpForAnotherUserAsync([PermissionCatalog.Payments.Read]);
        var limited = (await paymentsOnly.GetFromJsonAsync<ClinicsOverviewDto>($"{ClinicsOverview}?{range}"))!;
        limited.Clinics[0].Current.Should().Match<ClinicsOverviewFiguresDto>(f =>
            f.NetChargeAmount == null && f.NetCollectedAmount == 0m && f.VisitCount == null && f.ExaminationCount == null && f.Appointments == null);
        limited.Clinics[0].Outstanding.Should().BeNull("açık alacak ücret okuma izni ister");

        var tooLong = await http.GetAsync($"{ClinicsOverview}?from={Today.AddDays(-400):yyyy-MM-dd}&to={Today:yyyy-MM-dd}");
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(tooLong)).Should().Be("Registers.RangeTooLong");
    }
}
