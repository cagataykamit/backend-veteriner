using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.DailyTransactions;
using Backend.Veteriner.Application.Reports.Performance.Breakdowns;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Visits;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Reports;

/// <summary>
/// Gelişmiş raporlar (TIER-004; docs/PERFORMANCE_BREAKDOWNS_API_CONTRACT.md): kırılım toplamı gün sonu cirosuna eşittir, hekim ataması
/// ziyaret → kesinleştiren → atanmamış sırasıdır, bölümler izne göre boş döner, Temel pakette uç kapalıdır.
/// </summary>
public sealed partial class DailyTransactionsReportIntegrationTests
{
    private const string Breakdowns = "/api/v1/reports/performance-breakdowns";

    private static readonly string[] BreakdownPermissions =
    [
        PermissionCatalog.Payments.Read,
        PermissionCatalog.Charges.Read,
        PermissionCatalog.Examinations.Read,
        PermissionCatalog.Visits.Read,
        PermissionCatalog.Reports.Performance,
    ];

    private async Task SetPlanAsync(Guid tenantId, SubscriptionPlanCode plan)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var subscription = await db.TenantSubscriptions.SingleAsync(s => s.TenantId == tenantId);
        subscription.ActivatePaidPlan(plan, DateTime.UtcNow);
        await db.SaveChangesAsync();
    }

    private async Task<(Guid VetId, Guid FinalizerId)> SeedBreakdownDataAsync(Env env, Guid clientId, Guid petId)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var (vetId, _) = await Env.SeedUserAsync(db, hasher, env.TenantId, env.ClinicId);
        var (finalizerId, _) = await Env.SeedUserAsync(db, hasher, env.TenantId, env.ClinicId);

        var visit = new Visit(env.TenantId, env.ClinicId, petId, null, vetId, env.UserId, EarlierToday(TimeSpan.FromMinutes(40)));
        var withVisit = new Examination(env.TenantId, env.ClinicId, petId, null, EarlierToday(TimeSpan.FromMinutes(35)), "Kontrol", null, null, null, DateTime.UtcNow, visitId: visit.Id);
        var withoutVisit = new Examination(env.TenantId, env.ClinicId, petId, null, EarlierToday(TimeSpan.FromMinutes(20)), "Aşı", null, null, null, DateTime.UtcNow);
        withoutVisit.Finalize(finalizerId, DateTime.UtcNow).IsSuccess.Should().BeTrue();
        db.AddRange(visit, withVisit, withoutVisit);
        await db.SaveChangesAsync();

        await env.SeedChargeAsync(clientId, 500m, EarlierToday(TimeSpan.FromMinutes(30)), visit.Id);
        return (vetId, finalizerId);
    }

    [Fact]
    public async Task Breakdowns_Should_Be_Locked_On_Basic()
    {
        var env = await SeedTenantAsync();
        var http = await env.NewHttpForAnotherUserAsync(BreakdownPermissions);

        var response = await http.GetAsync($"{Breakdowns}?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("Subscriptions.FeatureNotInPlan");
        json.GetProperty("requiredPlanCode").GetString().Should().Be("Pro");

        (await http.GetAsync($"/api/v1/reports/performance-summary?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}"))
            .StatusCode.Should().Be(HttpStatusCode.OK, "bugünkü özet Temel'de kalır");
    }

    [Fact]
    public async Task Breakdowns_Should_Reconcile_With_Daily_Totals_And_Assign_Veterinarians()
    {
        var env = await SeedTenantAsync();
        await SetPlanAsync(env.TenantId, SubscriptionPlanCode.Pro);
        var client = await env.SeedClientAsync("Kırılım Acar");
        var pet = await env.SeedPetAsync(client);
        var (vetId, finalizerId) = await SeedBreakdownDataAsync(env, client, pet);
        await env.SeedChargeAsync(client, 200m, EarlierToday(TimeSpan.FromMinutes(10)));
        var http = await env.NewHttpForAnotherUserAsync(BreakdownPermissions);
        var range = $"from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}";

        var dto = await http.GetFromJsonAsync<PerformanceBreakdownsDto>($"{Breakdowns}?{range}&compare=true");
        var daily = await http.GetFromJsonAsync<DailyTransactionsReportResultDto>($"{Daily}?{range}&pageSize=1");

        var current = dto!.Current;
        current.ByVeterinarian!.Sum(v => v.NetChargeAmount).Should().Be(daily!.Totals.NetChargeAmount).And.Be(700m);
        current.ByVeterinarian.Single(v => v.VeterinarianUserId == vetId).Should().Match<VeterinarianBreakdownDto>(v => v.NetChargeAmount == 500m && v.ExaminationCount == 1);
        current.ByVeterinarian.Single(v => v.VeterinarianUserId == finalizerId).ExaminationCount.Should().Be(1);
        current.ByVeterinarian.Single(v => v.VeterinarianUserId == null).Should().Match<VeterinarianBreakdownDto>(v => v.NetChargeAmount == 200m && v.Name == "Atanmamış");
        current.ByLineKind!.Sum(l => l.NetChargeAmount).Should().Be(700m);
        current.TopItems!.Single().Should().Match<ChargeItemBreakdownDto>(i => i.Description == "Seed ucret" && i.ChargeCount == 2);
        current.BySpecies!.Sum(s => s.ExaminationCount).Should().Be(2);
        current.BySpecies.Sum(s => s.NetChargeAmount).Should().Be(700m);
        current.Activity.Should().Be(new PerformanceActivityDto(1, 1));
        dto.Previous!.To.Should().Be(Today.AddDays(-1));
        dto.Previous.ByVeterinarian.Should().BeEmpty();
    }

    [Fact]
    public async Task Breakdowns_Should_Hide_Sections_Without_Permission()
    {
        var env = await SeedTenantAsync();
        await SetPlanAsync(env.TenantId, SubscriptionPlanCode.Premium);
        var client = await env.SeedClientAsync("İzin Bal");
        var pet = await env.SeedPetAsync(client);
        await SeedBreakdownDataAsync(env, client, pet);
        var range = $"from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}&compare=false";

        var noPeople = await (await env.NewHttpForAnotherUserAsync([PermissionCatalog.Payments.Read, PermissionCatalog.Charges.Read, PermissionCatalog.Examinations.Read]))
            .GetFromJsonAsync<PerformanceBreakdownsDto>($"{Breakdowns}?{range}");
        noPeople!.Current.ByVeterinarian.Should().BeNull("hekim kırılımı kişi performansı izni ister");
        noPeople.Current.ByLineKind.Should().NotBeNull();
        noPeople.Current.Activity.Should().BeNull();
        noPeople.Previous.Should().BeNull();

        var examsOnly = await (await env.NewHttpForAnotherUserAsync([PermissionCatalog.Examinations.Read, PermissionCatalog.Reports.Performance]))
            .GetFromJsonAsync<PerformanceBreakdownsDto>($"{Breakdowns}?{range}");
        examsOnly!.Current.ByLineKind.Should().BeNull();
        examsOnly.Current.TopItems.Should().BeNull();
        examsOnly.Current.ByVeterinarian!.Should().OnlyContain(v => v.NetChargeAmount == null);
        examsOnly.Current.BySpecies!.Should().OnlyContain(s => s.NetChargeAmount == null);
    }
}
