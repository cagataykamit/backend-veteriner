using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.CashCounts;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Reports.DailyTransactions;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Reports;

/// <summary>
/// Günlük kasa kontrolü (TIER-008; docs/CASH_COUNT_API_CONTRACT.md): beklenen nakit aynı gün ve klinikle açılan kasa özetinin nakit
/// satırına eşittir; gün başına tek geçerli sayım; düzeltme eski kaydı iptal edip yenisini yazar; denetim kaydı; paket ve yetki.
/// </summary>
public sealed partial class DailyTransactionsReportIntegrationTests
{
    private const string CashCounts = "/api/v1/cash-counts";

    private static readonly string[] CashCountPermissions =
    [
        PermissionCatalog.CashCounts.Read,
        PermissionCatalog.CashCounts.Create,
        PermissionCatalog.CashCounts.Correct,
        PermissionCatalog.Payments.Read,
    ];

    [Fact]
    public async Task Cash_Count_Expected_Should_Equal_Cash_Summary_And_Corrections_Should_Keep_History()
    {
        var env = await SeedTenantAsync();
        await SetPlanAsync(env.TenantId, SubscriptionPlanCode.Premium);
        var counter = await env.NewHttpForAnotherUserAsync(CashCountPermissions);
        var a = await env.SeedClientAsync("Kasa Acar");
        var b = await env.SeedClientAsync("Kasa Bal");
        var chargeA = await env.SeedChargeAsync(a, 600m, EarlierToday(TimeSpan.FromMinutes(50)));
        var chargeB = await env.SeedChargeAsync(b, 300m, EarlierToday(TimeSpan.FromMinutes(45)));
        var chargeC = await env.SeedChargeAsync(b, 150m, EarlierToday(TimeSpan.FromMinutes(44)));
        await env.PayAsync(a, chargeA, 600m, PaymentMethod.Cash, EarlierToday(TimeSpan.FromMinutes(40)));
        await env.PayAsync(b, chargeB, 300m, PaymentMethod.Card, EarlierToday(TimeSpan.FromMinutes(35)));
        var voided = await env.PayAsync(b, chargeC, 150m, PaymentMethod.Cash, EarlierToday(TimeSpan.FromMinutes(30)));
        await env.SendAsync(HttpMethod.Post, $"/api/v1/payments/{voided}/void", new { reason = "Yanlis nakit girisi" }, HttpStatusCode.NoContent);
        await env.SellAsync(2m, 10m);
        var dayQuery = $"date={Today:yyyy-MM-dd}&clinicId={env.ClinicId}";

        var cashSummary = await env.GetAsync<CashSummaryReportDto>($"{Cash}?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}&clinicId={env.ClinicId}");
        var day = (await counter.GetFromJsonAsync<CashCountDayDto>($"{CashCounts}/day?{dayQuery}"))!;

        var cashRow = cashSummary.ByMethod.Single(m => m.PaymentMethod == PaymentMethod.Cash);
        day.Cash.Should().Be(cashRow, "beklenen nakit gün sonu kasa özetinin nakit satırıdır");
        day.ExpectedCashAmount.Should().Be(cashRow.NetAmount, "tek kasalı klinikte kasa hareketi yoksa beklenen nakit = nakit satırı");
        day.Cash.NetAmount.Should().Be(620m);
        day.Current.Should().BeNull();
        day.Previous.Should().BeNull();

        // Kayıt: açılış 200, sayılan 815 → olması gereken 820, fark −5.
        var create = await counter.PostAsJsonAsync(CashCounts, new
        {
            clinicId = env.ClinicId, businessDate = Today, openingAmount = 200m, countedAmount = 815m, expectedCashAmount = 620m,
            note = "Para üstü eksik verildi",
        });
        create.StatusCode.Should().Be(HttpStatusCode.OK, await create.Content.ReadAsStringAsync());
        var first = (await create.Content.ReadFromJsonAsync<CashCountDto>())!;
        first.ExpectedCashAmount.Should().Be(620m);
        first.ExpectedClosingAmount.Should().Be(820m);
        first.DifferenceAmount.Should().Be(-5m);
        first.CreatedByName.Should().NotBeNullOrWhiteSpace();

        var again = await counter.PostAsJsonAsync(CashCounts, new
        {
            clinicId = env.ClinicId, businessDate = Today, openingAmount = 200m, countedAmount = 820m, expectedCashAmount = 620m,
        });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(again)).Should().Be("CashCounts.DuplicateForDay");

        // Sayımdan sonra gelen nakit tahsilat: ekran uyarır, eski beklenen nakitle düzeltme reddedilir.
        var late = await env.SeedChargeAsync(a, 80m, EarlierToday(TimeSpan.FromMinutes(2)));
        await env.PayAsync(a, late, 80m, PaymentMethod.Cash, EarlierToday(TimeSpan.FromMinutes(1)));
        var changed = (await counter.GetFromJsonAsync<CashCountDayDto>($"{CashCounts}/day?{dayQuery}"))!;
        changed.Current!.Id.Should().Be(first.Id);
        changed.ExpectedChangedSinceCount.Should().BeTrue();
        changed.Cash.NetAmount.Should().Be(700m);

        var stale = await counter.PostAsJsonAsync($"{CashCounts}/{first.Id}/correct", new
        {
            openingAmount = 200m, countedAmount = 900m, expectedCashAmount = 620m, reason = "Geç gelen nakit tahsilat",
        });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(stale)).Should().Be("CashCounts.ExpectedChanged");

        var correct = await counter.PostAsJsonAsync($"{CashCounts}/{first.Id}/correct", new
        {
            openingAmount = 200m, countedAmount = 900m, expectedCashAmount = 700m, reason = "Geç gelen nakit tahsilat",
        });
        correct.StatusCode.Should().Be(HttpStatusCode.OK, await correct.Content.ReadAsStringAsync());
        var second = (await correct.Content.ReadFromJsonAsync<CashCountDto>())!;
        second.CorrectsCashCountId.Should().Be(first.Id);
        second.DifferenceAmount.Should().Be(0m);

        var afterCorrect = (await counter.GetFromJsonAsync<CashCountDayDto>($"{CashCounts}/day?{dayQuery}"))!;
        afterCorrect.Current!.Id.Should().Be(second.Id);
        afterCorrect.ExpectedChangedSinceCount.Should().BeFalse();
        afterCorrect.History.Select(h => h.Id).Should().Equal(second.Id, first.Id);
        var old = afterCorrect.History[1];
        old.IsVoided.Should().BeTrue();
        old.VoidReason.Should().Be("Geç gelen nakit tahsilat");
        old.CountedAmount.Should().Be(815m, "eski kayıt ezilmez");

        var range = $"from={Today.AddDays(-7):yyyy-MM-dd}&to={Today:yyyy-MM-dd}&clinicId={env.ClinicId}";
        (await counter.GetFromJsonAsync<PagedResult<CashCountDto>>($"{CashCounts}?{range}"))!.Items.Should().ContainSingle()
            .Which.Id.Should().Be(second.Id);
        (await counter.GetFromJsonAsync<PagedResult<CashCountDto>>($"{CashCounts}?{range}&includeVoided=true"))!.TotalItems.Should().Be(2);

        var voidResponse = await counter.PostAsJsonAsync($"{CashCounts}/{second.Id}/void", new { reason = "Yanlış klinik seçildi" });
        voidResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await counter.PostAsJsonAsync($"{CashCounts}/{second.Id}/void", new { reason = "Yanlış klinik seçildi" })).StatusCode
            .Should().Be(HttpStatusCode.Conflict);
        (await counter.GetFromJsonAsync<CashCountDayDto>($"{CashCounts}/day?{dayQuery}"))!.Current.Should().BeNull();

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var targets = new[] { $"CashCountId={first.Id}", $"CashCountId={second.Id}" };
        var audits = await db.AuditLogs
            .Where(l => l.Success && (targets.Contains(l.TargetId!) || (l.Action == "CashCount.Create" && l.TargetId!.Contains(env.ClinicId.ToString()))))
            .Select(l => l.Action)
            .ToListAsync();
        audits.Should().BeEquivalentTo(["CashCount.Create", "CashCount.Correct", "CashCount.Void"]);
    }

    [Fact]
    public async Task Cash_Count_Should_Be_Premium_To_Record_And_Follow_Permissions_And_Scope()
    {
        var env = await SeedTenantAsync();
        var other = await SeedTenantAsync();
        var counter = await env.NewHttpForAnotherUserAsync(CashCountPermissions);
        var body = new { clinicId = env.ClinicId, businessDate = Today, openingAmount = 0m, countedAmount = 0m, expectedCashAmount = 0m };

        // Temel (deneme dahil): okuma açık, kayıt kapalı.
        var locked = await counter.PostAsJsonAsync(CashCounts, body);
        locked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var json = await locked.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("Subscriptions.FeatureNotInPlan");
        json.GetProperty("requiredPlanCode").GetString().Should().Be("Pro");
        (await counter.GetAsync($"{CashCounts}/day?date={Today:yyyy-MM-dd}&clinicId={env.ClinicId}")).StatusCode.Should().Be(HttpStatusCode.OK);

        await SetPlanAsync(env.TenantId, SubscriptionPlanCode.Premium);

        var noClinic = await counter.GetAsync($"{CashCounts}/day?date={Today:yyyy-MM-dd}");
        noClinic.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(noClinic)).Should().Be("CashCounts.ClinicScopeRequired");
        var future = await counter.PostAsJsonAsync(CashCounts, body with { businessDate = Today.AddDays(1) });
        future.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(future)).Should().Be("CashCounts.Validation");

        var readOnly = await env.NewHttpForAnotherUserAsync([PermissionCatalog.CashCounts.Read]);
        (await readOnly.PostAsJsonAsync(CashCounts, body)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var creator = await env.NewHttpForAnotherUserAsync([PermissionCatalog.CashCounts.Create]);
        var created = await creator.PostAsJsonAsync(CashCounts, body);
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var count = (await created.Content.ReadFromJsonAsync<CashCountDto>())!;
        (await creator.PostAsJsonAsync($"{CashCounts}/{count.Id}/void", new { reason = "Yetkisiz iptal" })).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await _factory.CreateClient().GetAsync($"{CashCounts}/day?date={Today:yyyy-MM-dd}&clinicId={env.ClinicId}")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

        // Başka kiracı kaydı ne görür ne iptal eder.
        var foreign = await other.NewHttpForAnotherUserAsync(CashCountPermissions);
        await SetPlanAsync(other.TenantId, SubscriptionPlanCode.Premium);
        (await foreign.PostAsJsonAsync($"{CashCounts}/{count.Id}/void", new { reason = "Başka kiracı" })).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await foreign.GetAsync($"{CashCounts}/day?date={Today:yyyy-MM-dd}&clinicId={env.ClinicId}")).StatusCode
            .Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }
}
