using System.Net;
using System.Net.Http.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Reports.DailyTransactions;
using Backend.Veteriner.Application.Reports.DailyTransactions.Queries;
using Backend.Veteriner.Application.Reports.OutstandingBalances.Contracts.Dtos;
using Backend.Veteriner.Application.Reports.Performance;
using Backend.Veteriner.Domain.Payments;
using AwesomeAssertions;

namespace Backend.IntegrationTests.Reports;

/// <summary>
/// İşletme performansı özeti (REPORT-008 dilim 1): her sayı aynı süzgeçle açılan raporun toplamına eşittir, önceki dönem aynı
/// uzunluktadır, izni olmayan bölüm boş döner, aralık ve klinik kuralları gün sonu raporlarıyla aynıdır.
/// </summary>
public sealed partial class DailyTransactionsReportIntegrationTests
{
    private const string Performance = "/api/v1/reports/performance-summary";

    private static readonly string[] AllReportPermissions =
    [
        PermissionCatalog.Payments.Read,
        PermissionCatalog.Charges.Read,
        PermissionCatalog.Examinations.Read,
        PermissionCatalog.Vaccinations.Read,
        PermissionCatalog.Appointments.Read,
    ];

    [Fact]
    public async Task Performance_Summary_Should_Equal_Report_Totals_And_Compare_With_Previous_Period()
    {
        var env = await SeedTenantAsync();
        var a = await env.SeedClientAsync("Pano Acar");
        var b = await env.SeedClientAsync("Pano Bal");
        var chargeA = await env.SeedChargeAsync(a, 500m, EarlierToday(TimeSpan.FromMinutes(30)));
        await env.SeedChargeAsync(b, 250m, EarlierToday(TimeSpan.FromMinutes(25)));
        await env.PayAsync(a, chargeA, 300m, PaymentMethod.Card, EarlierToday(TimeSpan.FromMinutes(10)));
        var http = await env.NewHttpForAnotherUserAsync(AllReportPermissions);
        var range = $"from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}";

        var summary = await http.GetFromJsonAsync<PerformanceSummaryDto>($"{Performance}?{range}&compare=true");
        var daily = await http.GetFromJsonAsync<DailyTransactionsReportResultDto>($"{Daily}?{range}&pageSize=1");
        var cash = await http.GetFromJsonAsync<CashSummaryReportDto>($"{Cash}?{range}");
        var outstanding = await http.GetFromJsonAsync<OutstandingBalancesReportResultDto>("/api/v1/reports/outstanding-balances?pageSize=1");

        summary!.Currency.Should().Be("TRY");
        summary.Current.From.Should().Be(Today);
        summary.Current.Charges.Should().BeEquivalentTo(daily!.Totals);
        summary.Current.Charges!.NetChargeAmount.Should().Be(750m);
        summary.Current.Cash!.Totals.Should().BeEquivalentTo(cash!.Totals);
        summary.Current.Cash.Totals.NetAmount.Should().Be(300m);
        summary.Current.Cash.ByMethod.Single(m => m.PaymentMethod == PaymentMethod.Card).NetAmount.Should().Be(300m);
        summary.Current.Cash.ByUser.Should().BeEquivalentTo(cash.ByUser);
        summary.Current.ExaminationCount.Should().Be(0);
        summary.Current.Vaccinations.Should().BeEquivalentTo(new PerformanceVaccinationCountsDto(0, 0));
        summary.Current.Appointments!.Completed.Should().Be(0);
        summary.Outstanding!.TotalOutstandingAmount.Should().Be(outstanding!.TotalOutstandingAmount).And.Be(450m);
        summary.Outstanding.ClientCount.Should().Be(outstanding.TotalCount);

        summary.Previous!.From.Should().Be(Today.AddDays(-1));
        summary.Previous.To.Should().Be(Today.AddDays(-1));
        summary.Previous.Charges!.NetChargeAmount.Should().Be(0m);

        var withoutCompare = await http.GetFromJsonAsync<PerformanceSummaryDto>($"{Performance}?{range}&compare=false");
        withoutCompare!.Previous.Should().BeNull();
    }

    /// <summary>Şimdiden önce ama İstanbul günü içinde (gece yarısından hemen sonra koşuda dünkü güne düşmesin).</summary>
    private static DateTime EarlierToday(TimeSpan before)
    {
        var dayStart = OperationDayBounds.ForLocalDate(Today).DayStartUtc;
        var at = DateTime.UtcNow - before;
        return at < dayStart ? dayStart : at;
    }

    [Fact]
    public async Task Performance_Summary_Should_Hide_Sections_Without_Permission_And_Enforce_Ranges()
    {
        var env = await SeedTenantAsync();
        var paymentsOnly = await env.NewHttpForAnotherUserAsync([PermissionCatalog.Payments.Read]);
        var range = $"from={Today.AddDays(-29):yyyy-MM-dd}&to={Today:yyyy-MM-dd}";

        var summary = await paymentsOnly.GetFromJsonAsync<PerformanceSummaryDto>($"{Performance}?{range}");

        summary!.Current.Cash.Should().NotBeNull();
        summary.Current.Charges.Should().BeNull("ciro gün sonu dökümü gibi ücret iznini de ister");
        summary.Current.ExaminationCount.Should().BeNull();
        summary.Current.Vaccinations.Should().BeNull();
        summary.Current.Appointments.Should().BeNull();
        summary.Outstanding.Should().BeNull();
        summary.Previous!.From.Should().Be(Today.AddDays(-59), "varsayılan karşılaştırma açık, önceki 30 gün");
        summary.Previous.To.Should().Be(Today.AddDays(-30));

        var reversed = await paymentsOnly.GetAsync($"{Performance}?from={Today:yyyy-MM-dd}&to={Today.AddDays(-1):yyyy-MM-dd}");
        reversed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(reversed)).Should().Be("Registers.DateRangeInvalid");

        var tooLong = await paymentsOnly.GetAsync($"{Performance}?from={Today.AddDays(-400):yyyy-MM-dd}&to={Today:yyyy-MM-dd}");
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(tooLong)).Should().Be("Registers.RangeTooLong");
    }
}
