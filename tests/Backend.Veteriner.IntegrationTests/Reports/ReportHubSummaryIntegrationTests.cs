using System.Net;
using System.Net.Http.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Reports.DailyTransactions;
using Backend.Veteriner.Application.Reports.DailyTransactions.Queries;
using Backend.Veteriner.Application.Reports.Hub;
using Backend.Veteriner.Application.Reports.OutstandingBalances.Contracts.Dtos;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Reports;

/// <summary>
/// Rapor Merkezi ana ekran özeti (ADR-052, REPORT_HUB_SUMMARY_API_CONTRACT.md §8): her sayı aynı veriyi gösteren raporun toplamına eşittir,
/// izni olmayan bölüm boş döner, sayılar tek şubeye göredir, seri noktaları dönem kuralına uyar.
/// </summary>
public sealed partial class DailyTransactionsReportIntegrationTests
{
    private const string Hub = "/api/v1/reports/hub-summary";

    private static readonly string[] HubPermissions =
    [
        PermissionCatalog.Payments.Read,
        PermissionCatalog.Charges.Read,
        PermissionCatalog.CashRegisters.Read,
        PermissionCatalog.Examinations.Read,
        PermissionCatalog.Vaccinations.Read,
        PermissionCatalog.Appointments.Read,
        PermissionCatalog.Prescriptions.Read,
        PermissionCatalog.Products.Read,
        PermissionCatalog.Visits.Read,
    ];

    [Fact]
    public async Task Hub_Summary_Should_Equal_Report_Totals()
    {
        var env = await SeedTenantAsync();
        var a = await env.SeedClientAsync("Merkez Acar");
        var b = await env.SeedClientAsync("Merkez Bal");
        var chargeA = await env.SeedChargeAsync(a, 500m, EarlierToday(TimeSpan.FromMinutes(30)));
        await env.SeedChargeAsync(b, 250m, EarlierToday(TimeSpan.FromMinutes(25)));
        await env.PayAsync(a, chargeA, 300m, PaymentMethod.Card, EarlierToday(TimeSpan.FromMinutes(10)));
        await SeedLowStockAsync(env);
        var http = await env.NewHttpForAnotherUserAsync(HubPermissions);
        var range = $"from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}&clinicId={env.ClinicId}";

        var hub = await GetHubAsync(http, $"{Hub}?period=today&clinicId={env.ClinicId}");
        var daily = await http.GetFromJsonAsync<DailyTransactionsReportResultDto>($"{Daily}?{range}&pageSize=1");
        var cash = await http.GetFromJsonAsync<CashSummaryReportDto>($"{Cash}?{range}");
        var outstanding = await http.GetFromJsonAsync<OutstandingBalancesReportResultDto>($"/api/v1/reports/outstanding-balances?pageSize=1&clinicId={env.ClinicId}");
        var openItems = await http.GetFromJsonAsync<EndOfDayOpenItemsDto>($"{OpenItems}?{range}");
        var todayView = await http.GetFromJsonAsync<TodayDto>($"/api/v1/visits/today?clinicId={env.ClinicId}");

        hub.Period.Should().Be("today");
        hub.ClinicId.Should().Be(env.ClinicId);
        hub.Window.From.Should().Be(Today);
        hub.Window.PreviousFrom.Should().Be(Today.AddDays(-1));

        hub.Turnover!.NetChargeAmount.Should().Be(daily!.Totals.NetChargeAmount).And.Be(750m);
        hub.Turnover.PreviousNetChargeAmount.Should().Be(0m);
        hub.Turnover.Series.Should().HaveCount(7, "bugün dönemi son 7 günü çizer");
        hub.Turnover.Series[^1].Should().Be(new ReportHubSeriesPointDto(Today, Today, 750m));
        hub.Turnover.Series[0].From.Should().Be(Today.AddDays(-6));

        hub.Collections!.NetAmount.Should().Be(cash!.Totals.NetAmount).And.Be(300m);
        hub.Collections.CollectedCount.Should().Be(cash.Totals.CollectedCount);
        hub.Collections.ByMethod.Single(m => m.PaymentMethod == PaymentMethod.Card).NetAmount.Should().Be(300m);

        hub.TodayCash!.NetCollectedAmount.Should().Be(300m);
        hub.TodayCash.CollectedCount.Should().Be(1);
        hub.TodayCash.Last7Days.Select(p => p.Value).Should().Equal(0m, 0m, 0m, 0m, 0m, 0m, 300m);
        hub.TodayCash.MainRegisterBalance.Should().Be(0m, "kart tahsilatı kasaya girmez");

        hub.Outstanding!.TotalOutstandingAmount.Should().Be(outstanding!.TotalOutstandingAmount).And.Be(450m);
        hub.Outstanding.ClientCount.Should().Be(outstanding.TotalCount).And.Be(2);
        hub.Outstanding.MaxClientOutstandingAmount.Should().Be(250m);

        hub.Appointments!.TotalCount.Should().Be(0);
        hub.Appointments.Series.Should().HaveCount(7);
        hub.Examinations!.Count.Should().Be(0);
        hub.Vaccinations.Should().Be(new ReportHubVaccinationsDto(0, 0));

        hub.Attention.EndOfDayUnchargedVisitCount.Should().Be(openItems!.OpenItems.UnchargedVisitCount);
        hub.Attention.HbsNotEnteredPrescriptions!.Count.Should().Be(todayView!.OpenItems!.HbsNotEnteredPrescriptionCount!.Value);
        hub.Attention.HbsNotEnteredVaccinations!.Count.Should().Be(todayView.OpenItems.HbsNotEnteredVaccinationCount!.Value);
        hub.Attention.HbsNotEnteredPrescriptions.OldestAtUtc.Should().BeNull();
        hub.Attention.OverdueVaccinationCount.Should().Be(0);
        hub.Attention.LowStockProductCount.Should().Be(1, "yalnız asgari seviyenin altındaki stok sayılır");
        hub.Attention.OverdueAppointmentCount.Should().Be(todayView.OpenItems.OverdueAppointmentCount!.Value);
    }

    [Fact]
    public async Task Hub_Summary_Should_Hide_Sections_Without_Permission()
    {
        var env = await SeedTenantAsync();
        var paymentsOnly = await env.NewHttpForAnotherUserAsync([PermissionCatalog.Payments.Read]);
        var unrelated = await env.NewHttpForAnotherUserAsync([PermissionCatalog.Clients.Read]);

        var summary = await GetHubAsync(paymentsOnly, $"{Hub}?period=month&clinicId={env.ClinicId}");

        summary.TodayCash.Should().NotBeNull();
        summary.TodayCash!.MainRegisterBalance.Should().BeNull("kasa bakiyesi kasa okuma izni ister");
        summary.Collections.Should().NotBeNull();
        summary.Turnover.Should().BeNull("ciro gün sonu dökümü gibi ücret iznini de ister");
        summary.Outstanding.Should().BeNull();
        summary.Appointments.Should().BeNull();
        summary.Examinations.Should().BeNull();
        summary.Vaccinations.Should().BeNull();
        summary.Attention.Should().Be(new ReportHubAttentionDto(null, null, null, null, null, null));

        var none = await GetHubAsync(unrelated, $"{Hub}?period=week&clinicId={env.ClinicId}");
        none.TodayCash.Should().BeNull();
        none.Collections.Should().BeNull();
        none.Attention.Should().Be(new ReportHubAttentionDto(null, null, null, null, null, null));
    }

    [Fact]
    public async Task Hub_Summary_Should_Require_A_Known_Period_And_An_Accessible_Clinic()
    {
        var env = await SeedTenantAsync();
        var other = await SeedTenantAsync();

        var noPeriod = await env.Http.GetAsync($"{Hub}?clinicId={env.ClinicId}");
        noPeriod.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(noPeriod)).Should().Be("ReportsHub.PeriodInvalid");

        var badPeriod = await env.Http.GetAsync($"{Hub}?period=year&clinicId={env.ClinicId}");
        (await IntegrationTestProblemDetails.ReadCodeAsync(badPeriod)).Should().Be("ReportsHub.PeriodInvalid");

        var noClinic = await env.Http.GetAsync($"{Hub}?period=today");
        noClinic.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(noClinic)).Should().Be("ReportsHub.ClinicScopeRequired");

        var foreignClinic = await other.Http.GetAsync($"{Hub}?period=today&clinicId={env.ClinicId}");
        foreignClinic.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);

        (await _factory.CreateClient().GetAsync($"{Hub}?period=today&clinicId={env.ClinicId}")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Hub_Summary_Series_Should_Follow_The_Period()
    {
        var env = await SeedTenantAsync();
        var http = await env.NewHttpForAnotherUserAsync(HubPermissions);

        var week = await GetHubAsync(http, $"{Hub}?period=week&clinicId={env.ClinicId}");
        var monday = Today.AddDays(-(((int)Today.DayOfWeek + 6) % 7));
        week.Window.From.Should().Be(monday);
        week.Window.PreviousFrom.Should().Be(monday.AddDays(-7));
        week.Examinations!.Series.Select(p => p.From).Should().Equal(Enumerable.Range(0, Today.DayNumber - monday.DayNumber + 1).Select(monday.AddDays));

        var month = await GetHubAsync(http, $"{Hub}?period=month&clinicId={env.ClinicId}");
        var first = new DateOnly(Today.Year, Today.Month, 1);
        month.Window.From.Should().Be(first);
        month.Window.PreviousFrom.Should().Be(first.AddMonths(-1));
        month.Appointments!.Series[0].From.Should().Be(first);
        month.Appointments.Series[^1].To.Should().Be(Today);
        month.Appointments.Series.Skip(1).Should().OnlyContain(p => p.From.DayOfWeek == DayOfWeek.Monday);
    }

    private static async Task<ReportHubSummaryDto> GetHubAsync(HttpClient http, string url)
    {
        var response = await http.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ReportHubSummaryDto>())!;
    }

    /// <summary>Asgari seviyenin altında bir stok (Stok durumu "Düşük stok" süzgecine düşer).</summary>
    private async Task SeedLowStockAsync(Env env)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = new Product(env.TenantId, $"hub-{Guid.NewGuid():N}"[..14], "Adet", 5.00m, "TRY", DateTime.UtcNow, sku: $"H1-{Guid.NewGuid():N}"[..14]);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        db.ProductStocks.Add(new ProductStock(env.TenantId, env.ClinicId, product.Id, 1m, 5m, DateTime.UtcNow));
        await db.SaveChangesAsync();
    }
}
