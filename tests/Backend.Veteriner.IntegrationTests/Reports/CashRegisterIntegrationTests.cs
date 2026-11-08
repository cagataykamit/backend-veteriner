using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.CashCounts;
using Backend.Veteriner.Application.CashRegisters;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Payments.Contracts.Dtos;
using Backend.Veteriner.Application.Reports.DailyTransactions;
using Backend.Veteriner.Domain.CashRegisters;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Reports;

/// <summary>
/// Çoklu kasa (TIER-008; docs/CASH_REGISTERS_API_CONTRACT.md): kasa bazında beklenen nakit, giriş/çıkış ve aktarım (iptali iki taraf),
/// pasif kasa, paket kilidi ve Ana kasa, eski ödeme isteği, kiracı/klinik izolasyonu, yetki, rapor devreden bakiyesi, geriye dönük doldurma.
/// </summary>
public sealed partial class DailyTransactionsReportIntegrationTests
{
    private const string Registers = "/api/v1/cash-registers";
    private const string Movements = "/api/v1/cash-movements";

    private static readonly string[] CashManagerPermissions =
    [
        PermissionCatalog.CashRegisters.Read,
        PermissionCatalog.CashRegisters.Manage,
        PermissionCatalog.CashMovements.Create,
        PermissionCatalog.CashMovements.Void,
        PermissionCatalog.CashCounts.Read,
        PermissionCatalog.CashCounts.Create,
        PermissionCatalog.Payments.Read,
        PermissionCatalog.Payments.Create,
    ];

    private static readonly string[] ReceptionPermissions =
    [
        PermissionCatalog.CashRegisters.Read,
        PermissionCatalog.CashMovements.Create,
        PermissionCatalog.CashCounts.Read,
        PermissionCatalog.Payments.Create,
    ];

    [Fact]
    public async Task Registers_Should_Split_Expected_Cash_And_Transfers_Should_Void_Both_Legs()
    {
        var env = await SeedTenantAsync();
        await SetPlanAsync(env.TenantId, SubscriptionPlanCode.Pro);
        var manager = await env.NewHttpForAnotherUserAsync(CashManagerPermissions);
        var reception = await env.NewHttpForAnotherUserAsync(ReceptionPermissions);

        var drawer = await PostOkAsync<CashRegisterDto>(manager, Registers, new { clinicId = env.ClinicId, code = "k2", name = "Arka kasa" });
        drawer.Code.Should().Be("K2");
        var registers = (await manager.GetFromJsonAsync<List<CashRegisterDto>>($"{Registers}?clinicId={env.ClinicId}"))!;
        registers.Select(r => r.Name).Should().Equal(CashRegister.MainName, "Arka kasa");
        var main = registers[0];
        main.IsMain.Should().BeTrue();
        var category = (await manager.GetFromJsonAsync<List<CashMovementCategoryDto>>($"{Movements}/categories"))!.FirstOrDefault()
                       ?? await PostOkAsync<CashMovementCategoryDto>(manager, $"{Movements}/categories", new { name = "Masraf" });

        var a = await env.SeedClientAsync("Kasa Ayar");
        await env.PayAsync(a, await env.SeedChargeAsync(a, 600m, EarlierToday(TimeSpan.FromMinutes(50))), 600m, PaymentMethod.Cash, EarlierToday(TimeSpan.FromMinutes(40)));
        var drawerPayment = await PayToRegisterAsync(env, a, 300m, PaymentMethod.Cash, drawer.Id);
        await env.PayAsync(a, await env.SeedChargeAsync(a, 200m, EarlierToday(TimeSpan.FromMinutes(30))), 200m, PaymentMethod.Card, EarlierToday(TimeSpan.FromMinutes(20)));
        (await PayToRegisterRawAsync(env, a, 50m, PaymentMethod.Card, drawer.Id)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "kart kasaya bağlanmaz");

        var outMovement = await PostOkAsync<CashMovementDto>(reception, Movements, new
        {
            clinicId = env.ClinicId, cashRegisterId = drawer.Id, direction = "Out", businessDate = Today, amount = 50m, categoryId = category.Id,
            description = "Kargo",
        });
        var transfer = await PostOkAsync<CashMovementDto>(reception, $"{Movements}/transfers", new
        {
            clinicId = env.ClinicId, sourceCashRegisterId = main.Id, targetCashRegisterId = drawer.Id, businessDate = Today, amount = 100m,
        });
        transfer.Kind.Should().Be(CashMovementKind.TransferOut);
        transfer.CounterpartCashRegisterName.Should().Be("Arka kasa");

        var mainDay = await DayAsync(manager, env, main.Id);
        mainDay.Cash.NetAmount.Should().Be(600m);
        mainDay.Movements.TransferOutAmount.Should().Be(100m);
        mainDay.ExpectedCashAmount.Should().Be(500m);
        var drawerDay = await DayAsync(manager, env, drawer.Id);
        drawerDay.Cash.NetAmount.Should().Be(300m);
        drawerDay.ExpectedCashAmount.Should().Be(350m, "300 tahsilat − 50 çıkış + 100 gelen aktarım");
        var cashRow = (await env.GetAsync<CashSummaryReportDto>($"{Cash}?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}&clinicId={env.ClinicId}"))
            .ByMethod.Single(m => m.PaymentMethod == PaymentMethod.Cash);
        cashRow.NetAmount.Should().Be(900m, "gün sonu kasa özeti tüm kasaların nakdidir, değişmedi");
        (mainDay.Cash.NetAmount + drawerDay.Cash.NetAmount).Should().Be(cashRow.NetAmount);

        // Sayım kasa seçerek yapılır; giriş/çıkış ve aktarım artık "fark" değil kendi satırında.
        var count = await PostOkAsync<CashCountDto>(manager, "/api/v1/cash-counts", new
        {
            clinicId = env.ClinicId, cashRegisterId = drawer.Id, businessDate = Today, openingAmount = 0m, countedAmount = 350m, expectedCashAmount = 350m,
        });
        count.CashRegisterId.Should().Be(drawer.Id);
        count.DifferenceAmount.Should().Be(0m);

        (await reception.PostAsJsonAsync($"{Movements}/{transfer.Id}/void", new { reason = "Yanlış kasa" })).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "iptal yönetici düzeyinde");
        (await manager.PostAsJsonAsync($"{Movements}/{transfer.Id}/void", new { reason = "Yanlış kasa seçildi" })).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        (await manager.PostAsJsonAsync($"{Movements}/{transfer.Id}/void", new { reason = "Yanlış kasa seçildi" })).StatusCode
            .Should().Be(HttpStatusCode.Conflict);
        (await DayAsync(manager, env, main.Id)).ExpectedCashAmount.Should().Be(600m);
        var afterVoid = await DayAsync(manager, env, drawer.Id);
        afterVoid.ExpectedCashAmount.Should().Be(250m, "aktarımın iki bacağı birlikte iptal edildi");
        afterVoid.ExpectedChangedSinceCount.Should().BeTrue();

        var listed = (await manager.GetFromJsonAsync<PagedResult<CashMovementDto>>(
            $"{Movements}?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}&clinicId={env.ClinicId}&includeVoided=true"))!;
        listed.Items.Where(m => m.TransferId == transfer.TransferId).Should().HaveCount(2).And.OnlyContain(m => m.IsVoided);
        listed.Items.Single(m => m.Id == outMovement.Id).CategoryName.Should().Be(category.Name);

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Payments.SingleAsync(p => p.Id == drawerPayment)).CashRegisterId.Should().Be(drawer.Id);
        var drawerKey = drawer.Id.ToString();
        var clinicKey = env.ClinicId.ToString();
        var transferKey = $"CashMovementId={transfer.Id}";
        var audits = await db.AuditLogs
            .Where(l => l.Success && (l.TargetId == transferKey || l.TargetId!.Contains(drawerKey) || l.TargetId!.Contains(clinicKey)))
            .Select(l => l.Action)
            .ToListAsync();
        audits.Should().Contain(["CashRegister.Create", "CashMovement.Create", "CashTransfer.Create", "CashMovement.Void"]);
    }

    [Fact]
    public async Task Inactive_Register_And_Basic_Plan_Should_Leave_Only_The_Main_Register()
    {
        var env = await SeedTenantAsync();
        await SetPlanAsync(env.TenantId, SubscriptionPlanCode.Premium);
        var manager = await env.NewHttpForAnotherUserAsync(CashManagerPermissions);
        var drawer = await PostOkAsync<CashRegisterDto>(manager, Registers, new { clinicId = env.ClinicId, code = "K2", name = "Arka kasa" });
        var main = (await manager.GetFromJsonAsync<List<CashRegisterDto>>($"{Registers}?clinicId={env.ClinicId}"))!.Single(r => r.IsMain);
        var a = await env.SeedClientAsync("Pasif Kasa");

        (await manager.PostAsJsonAsync(Registers, new { clinicId = env.ClinicId, code = "k2", name = "Tekrar" })).StatusCode
            .Should().Be(HttpStatusCode.Conflict, "kod klinikte benzersiz");
        var mainLocked = await manager.PutAsJsonAsync($"{Registers}/{main.Id}", new { code = "ANA", name = "Ana kasa", isActive = false });
        mainLocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(mainLocked)).Should().Be("CashRegisters.MainRegisterLocked");

        (await manager.PutAsJsonAsync($"{Registers}/{drawer.Id}", new { code = "K2", name = "Arka kasa", isActive = false })).StatusCode
            .Should().Be(HttpStatusCode.OK);
        var inactivePayment = await PayToRegisterRawAsync(env, a, 40m, PaymentMethod.Cash, drawer.Id);
        inactivePayment.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(inactivePayment)).Should().Be(CashRegister.InactiveCode);
        var inactiveTransfer = await manager.PostAsJsonAsync($"{Movements}/transfers", new
        {
            clinicId = env.ClinicId, sourceCashRegisterId = main.Id, targetCashRegisterId = drawer.Id, businessDate = Today, amount = 10m,
        });
        (await IntegrationTestProblemDetails.ReadCodeAsync(inactiveTransfer)).Should().Be(CashRegister.InactiveCode);
        (await manager.GetFromJsonAsync<List<CashRegisterDto>>($"{Registers}?clinicId={env.ClinicId}&includeInactive=true"))!
            .Should().HaveCount(2, "pasif kasa silinmez");
        (await manager.PutAsJsonAsync($"{Registers}/{drawer.Id}", new { code = "K2", name = "Arka kasa", isActive = true })).EnsureSuccessStatusCode();

        // Paket düşünce: kayıtlar okunur, yeni işlem yalnız Ana kasaya.
        await SetPlanAsync(env.TenantId, SubscriptionPlanCode.Basic);
        var locked = await manager.PostAsJsonAsync(Registers, new { clinicId = env.ClinicId, code = "K3", name = "Yeni" });
        locked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var json = await locked.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("Subscriptions.FeatureNotInPlan");
        json.GetProperty("requiredPlanCode").GetString().Should().Be("Pro");
        (await PayToRegisterRawAsync(env, a, 40m, PaymentMethod.Cash, drawer.Id)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await PayToRegisterRawAsync(env, a, 40m, PaymentMethod.Cash, main.Id)).StatusCode.Should().Be(HttpStatusCode.Created, "Ana kasa her pakette");
        (await manager.PostAsJsonAsync(Movements, new
        {
            clinicId = env.ClinicId, cashRegisterId = main.Id, direction = "In", businessDate = Today, amount = 5m, categoryId = Guid.NewGuid(),
        })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Eski istek (cashRegisterId yok) bugünkü gibi yazılır; kasası boş nakit Ana kasanındır.
        var legacy = await env.PayAsync(a, await env.SeedChargeAsync(a, 70m, EarlierToday(TimeSpan.FromMinutes(5))), 70m, PaymentMethod.Cash, EarlierToday(TimeSpan.FromMinutes(4)));
        (await manager.GetAsync($"{Registers}?clinicId={env.ClinicId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await manager.GetAsync($"{Registers}/report?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}&clinicId={env.ClinicId}")).StatusCode
            .Should().Be(HttpStatusCode.OK, "okuma paket düşünce de açık");

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Payments.SingleAsync(p => p.Id == legacy)).CashRegisterId.Should().BeNull();
        var day = (await manager.GetFromJsonAsync<CashCountDayDto>($"/api/v1/cash-counts/day?date={Today:yyyy-MM-dd}&clinicId={env.ClinicId}&cashRegisterId={main.Id}"))!;
        day.Cash.CollectedAmount.Should().Be(110m, "Ana kasaya seçilerek 40 ve kasasız 70 nakit");
    }

    [Fact]
    public async Task Cash_Registers_Should_Not_Leak_Across_Tenants_And_Follow_Permissions()
    {
        var env = await SeedTenantAsync();
        var other = await SeedTenantAsync();
        await SetPlanAsync(env.TenantId, SubscriptionPlanCode.Pro);
        await SetPlanAsync(other.TenantId, SubscriptionPlanCode.Pro);
        var manager = await env.NewHttpForAnotherUserAsync(CashManagerPermissions);
        var reception = await env.NewHttpForAnotherUserAsync(ReceptionPermissions);
        var foreign = await other.NewHttpForAnotherUserAsync(CashManagerPermissions);
        var drawer = await PostOkAsync<CashRegisterDto>(manager, Registers, new { clinicId = env.ClinicId, code = "K2", name = "Arka kasa" });
        var main = (await manager.GetFromJsonAsync<List<CashRegisterDto>>($"{Registers}?clinicId={env.ClinicId}"))!.Single(r => r.IsMain);
        var transfer = await PostOkAsync<CashMovementDto>(reception, $"{Movements}/transfers", new
        {
            clinicId = env.ClinicId, sourceCashRegisterId = main.Id, targetCashRegisterId = drawer.Id, businessDate = Today, amount = 20m,
        });

        (await reception.PostAsJsonAsync(Registers, new { clinicId = env.ClinicId, code = "K3", name = "Yetkisiz" })).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        var noRead = await env.NewHttpForAnotherUserAsync([PermissionCatalog.Payments.Create]);
        (await noRead.GetAsync($"{Registers}?clinicId={env.ClinicId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _factory.CreateClient().GetAsync($"{Registers}?clinicId={env.ClinicId}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await foreign.PostAsJsonAsync($"{Movements}/{transfer.Id}/void", new { reason = "Başka kiracı" })).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await foreign.PutAsJsonAsync($"{Registers}/{drawer.Id}", new { code = "X", name = "X", isActive = true })).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await foreign.GetAsync($"{Registers}?clinicId={env.ClinicId}")).StatusCode
            .Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        var foreignPayment = await PayToRegisterRawAsync(other, await other.SeedClientAsync("Yabancı"), 10m, PaymentMethod.Cash, drawer.Id);
        foreignPayment.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(foreignPayment)).Should().Be("CashRegisters.NotFound");

        // Aynı kiracının başka kliniğindeki kasa da bulunmaz (aktarım klinik içidir).
        Guid secondClinicId;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clinic = new Clinic(env.TenantId, $"Dtr-{Guid.NewGuid():N}"[..14], "Izmir");
            db.Clinics.Add(clinic);
            db.CashRegisters.Add(CashRegister.CreateMain(env.TenantId, clinic.Id, DateTime.UtcNow));
            await db.SaveChangesAsync();
            secondClinicId = await db.CashRegisters.Where(r => r.ClinicId == clinic.Id).Select(r => r.Id).SingleAsync();
        }

        var crossClinic = await manager.PostAsJsonAsync($"{Movements}/transfers", new
        {
            clinicId = env.ClinicId, sourceCashRegisterId = main.Id, targetCashRegisterId = secondClinicId, businessDate = Today, amount = 10m,
        });
        crossClinic.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Register_Report_Should_Carry_Over_The_Balance_And_Export()
    {
        var env = await SeedTenantAsync();
        await SetPlanAsync(env.TenantId, SubscriptionPlanCode.Pro);
        var manager = await env.NewHttpForAnotherUserAsync(CashManagerPermissions);
        var drawer = await PostOkAsync<CashRegisterDto>(manager, Registers, new { clinicId = env.ClinicId, code = "K2", name = "Arka kasa" });
        var main = (await manager.GetFromJsonAsync<List<CashRegisterDto>>($"{Registers}?clinicId={env.ClinicId}"))!.Single(r => r.IsMain);
        var category = await PostOkAsync<CashMovementCategoryDto>(manager, $"{Movements}/categories", new { name = $"Masraf-{Guid.NewGuid():N}"[..14] });
        var a = await env.SeedClientAsync("Devir Acar");
        var past = DateTime.UtcNow.AddDays(-3);

        // Dönem öncesi: Ana kasaya 500 nakit, 3 gün önce 80 çıkış → devreden 420.
        await env.PayAsync(a, await env.SeedChargeAsync(a, 500m, past.AddMinutes(-10)), 500m, PaymentMethod.Cash, past);
        await PostOkAsync<CashMovementDto>(manager, Movements, new
        {
            clinicId = env.ClinicId, cashRegisterId = main.Id, direction = "Out", businessDate = Today.AddDays(-3), amount = 80m, categoryId = category.Id,
        });
        // Dönem içi: 200 nakit, 150 Arka kasaya aktarım, 30 giriş.
        await env.PayAsync(a, await env.SeedChargeAsync(a, 200m, EarlierToday(TimeSpan.FromMinutes(30))), 200m, PaymentMethod.Cash, EarlierToday(TimeSpan.FromMinutes(20)));
        await PostOkAsync<CashMovementDto>(manager, $"{Movements}/transfers", new
        {
            clinicId = env.ClinicId, sourceCashRegisterId = main.Id, targetCashRegisterId = drawer.Id, businessDate = Today, amount = 150m,
        });
        await PostOkAsync<CashMovementDto>(manager, Movements, new
        {
            clinicId = env.ClinicId, cashRegisterId = main.Id, direction = "In", businessDate = Today, amount = 30m, categoryId = category.Id,
        });

        var range = $"from={Today.AddDays(-1):yyyy-MM-dd}&to={Today:yyyy-MM-dd}&clinicId={env.ClinicId}";
        var report = (await manager.GetFromJsonAsync<CashRegisterReportDto>($"{Registers}/report?{range}&cashRegisterId={main.Id}"))!;
        report.OpeningBalance.Should().Be(420m);
        report.Totals.NetAmount.Should().Be(80m, "200 − 150 + 30");
        report.ClosingBalance.Should().Be(500m);
        report.Rows.Select(r => r.Kind).Should().BeEquivalentTo(
            [CashRegisterReportRowKind.Collection, CashRegisterReportRowKind.TransferOut, CashRegisterReportRowKind.In]);
        report.Rows[^1].Balance.Should().Be(report.ClosingBalance);

        var all = (await manager.GetFromJsonAsync<CashRegisterReportDto>($"{Registers}/report?{range}"))!;
        all.OpeningBalance.Should().Be(420m);
        all.ClosingBalance.Should().Be(650m, "aktarım klinik toplamını değiştirmez: 420 + 200 + 30");

        var csv = await manager.GetAsync($"{Registers}/report/export?{range}&cashRegisterId={main.Id}");
        csv.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await csv.Content.ReadAsStringAsync();
        text.Should().Contain("Devreden bakiye").And.Contain("Dönem sonu bakiye").And.Contain("Giden aktarım");
        text.Should().Contain("Tarih ve saat").And.NotContain(";Saat;", "ikinci sütun tarih de içerir");
        var xlsx = await manager.GetAsync($"{Registers}/report/export-xlsx?{range}");
        xlsx.StatusCode.Should().Be(HttpStatusCode.OK);
        xlsx.Content.Headers.ContentType!.MediaType.Should().Contain("spreadsheet");
    }

    [Fact]
    public async Task Payment_Read_Should_Carry_The_Cash_Register_And_Report_Should_Name_Users_Alike()
    {
        var env = await SeedTenantAsync();
        await SetPlanAsync(env.TenantId, SubscriptionPlanCode.Pro);
        var manager = await env.NewHttpForAnotherUserAsync(CashManagerPermissions);
        var drawer = await PostOkAsync<CashRegisterDto>(manager, Registers, new { clinicId = env.ClinicId, code = "K2", name = "Arka kasa" });
        var main = (await manager.GetFromJsonAsync<List<CashRegisterDto>>($"{Registers}?clinicId={env.ClinicId}"))!.Single(r => r.IsMain);
        var category = await PostOkAsync<CashMovementCategoryDto>(manager, $"{Movements}/categories", new { name = $"Masraf-{Guid.NewGuid():N}"[..14] });
        var a = await env.SeedClientAsync("Kasa Adı");

        var legacyCash = await env.PayAsync(a, await env.SeedChargeAsync(a, 100m, EarlierToday(TimeSpan.FromMinutes(40))), 100m, PaymentMethod.Cash, EarlierToday(TimeSpan.FromMinutes(35)));
        var drawerCash = await PayToRegisterAsync(env, a, 60m, PaymentMethod.Cash, drawer.Id);
        var card = await env.PayAsync(a, await env.SeedChargeAsync(a, 40m, EarlierToday(TimeSpan.FromMinutes(30))), 40m, PaymentMethod.Card, EarlierToday(TimeSpan.FromMinutes(25)));
        await PostOkAsync<CashMovementDto>(manager, Movements, new
        {
            clinicId = env.ClinicId, cashRegisterId = main.Id, direction = "In", businessDate = Today, amount = 10m, categoryId = category.Id,
        });

        async Task<PaymentDetailDto> DetailAsync(Guid id) => (await manager.GetFromJsonAsync<PaymentDetailDto>($"/api/v1/payments/{id}"))!;
        var legacy = await DetailAsync(legacyCash);
        legacy.CashRegisterId.Should().Be(main.Id, "kasası boş nakit kliniğin Ana kasasınındır");
        legacy.CashRegisterName.Should().Be(CashRegister.MainName);
        (await DetailAsync(drawerCash)).CashRegisterName.Should().Be("Arka kasa");
        var cardDetail = await DetailAsync(card);
        cardDetail.CashRegisterId.Should().BeNull("kart kasaya bağlanmaz");
        cardDetail.CashRegisterName.Should().BeNull();

        var list = (await manager.GetFromJsonAsync<PagedResult<PaymentListItemDto>>($"/api/v1/payments?clinicId={env.ClinicId}&page=1&pageSize=50"))!;
        list.Items.Single(p => p.Id == legacyCash).CashRegisterName.Should().Be(CashRegister.MainName);
        list.Items.Single(p => p.Id == drawerCash).CashRegisterId.Should().Be(drawer.Id);
        list.Items.Single(p => p.Id == card).CashRegisterName.Should().BeNull();

        // İşlemi yapan: tahsilat ve kasa hareketi aynı kural (görünen ad, yoksa e-postanın @ öncesi; e-posta yazılmaz).
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var collector = await db.Users.SingleAsync(u => u.Id == env.UserId);
            collector.SetDisplayName("Resepsiyon Ayşe", DateTime.UtcNow).IsSuccess.Should().BeTrue();
            await db.SaveChangesAsync();
        }

        var report = (await manager.GetFromJsonAsync<CashRegisterReportDto>(
            $"{Registers}/report?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}&clinicId={env.ClinicId}"))!;
        report.Rows.Where(r => r.Kind == CashRegisterReportRowKind.Collection).Should().HaveCount(2)
            .And.OnlyContain(r => r.PerformedByName == "Resepsiyon Ayşe");
        var movementBy = report.Rows.Single(r => r.Kind == CashRegisterReportRowKind.In).PerformedByName;
        movementBy.Should().StartWith("dtr-user-").And.NotContain("@");

        var daily = await env.GetAsync<DailyTransactionsReportResultDto>(
            $"{Daily}?{Range(Today, Today)}&clinicId={env.ClinicId}&pageSize=200");
        daily.Items.Where(i => i.Kind == DailyTransactionKind.Payment).Should().OnlyContain(i => i.PerformedByName == "Resepsiyon Ayşe");
        daily.Items.Where(i => i.Kind == DailyTransactionKind.Charge).Should().OnlyContain(i => i.PerformedByName == null || !i.PerformedByName.Contains('@'));
    }

    [Fact]
    public async Task Backfill_Should_Create_Main_Register_And_Link_Old_Cash_Once()
    {
        var env = await SeedTenantAsync();
        Guid cashPaymentId;
        Guid cardPaymentId;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var client = await env.SeedClientAsync("Eski Kayıt");
            var cash = new Payment(env.TenantId, env.ClinicId, client, null, null, null, 120m, "TRY", PaymentMethod.Cash, DateTime.UtcNow, null);
            var card = new Payment(env.TenantId, env.ClinicId, client, null, null, null, 80m, "TRY", PaymentMethod.Card, DateTime.UtcNow, null);
            db.Payments.AddRange(cash, card);
            await db.SaveChangesAsync();
            cashPaymentId = cash.Id;
            cardPaymentId = card.Id;

            await db.Database.ExecuteSqlRawAsync(CashRegisterBackfill.Sql);
            await db.Database.ExecuteSqlRawAsync(CashRegisterBackfill.Sql);
        }

        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var mains = await db.CashRegisters.IgnoreQueryFilters().Where(r => r.ClinicId == env.ClinicId && r.IsMain).ToListAsync();
            mains.Should().ContainSingle("tekrar çalıştırma ikinci Ana kasa açmaz");
            (await db.Payments.IgnoreQueryFilters().SingleAsync(p => p.Id == cashPaymentId)).CashRegisterId.Should().Be(mains[0].Id);
            (await db.Payments.IgnoreQueryFilters().SingleAsync(p => p.Id == cardPaymentId)).CashRegisterId.Should().BeNull();
            (await db.CashMovementCategories.IgnoreQueryFilters().CountAsync(c => c.TenantId == env.TenantId)).Should().Be(4);
        }
    }

    private static async Task<T> PostOkAsync<T>(HttpClient http, string url, object body)
    {
        var response = await http.PostAsJsonAsync(url, body);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<CashCountDayDto> DayAsync(HttpClient http, Env env, Guid registerId)
        => (await http.GetFromJsonAsync<CashCountDayDto>($"/api/v1/cash-counts/day?date={Today:yyyy-MM-dd}&clinicId={env.ClinicId}&cashRegisterId={registerId}"))!;

    private static async Task<Guid> PayToRegisterAsync(Env env, Guid clientId, decimal amount, PaymentMethod method, Guid registerId)
    {
        var response = await PayToRegisterRawAsync(env, clientId, amount, method, registerId);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<HttpResponseMessage> PayToRegisterRawAsync(Env env, Guid clientId, decimal amount, PaymentMethod method, Guid registerId)
    {
        var chargeId = await env.SeedChargeAsync(clientId, amount, EarlierToday(TimeSpan.FromMinutes(15)));
        return await env.Http.PostAsJsonAsync("/api/v1/payments", new
        {
            ClinicId = env.ClinicId,
            ClientId = clientId,
            Amount = amount,
            Currency = "TRY",
            Method = method,
            PaidAtUtc = EarlierToday(TimeSpan.FromMinutes(10)),
            RequestId = Guid.NewGuid(),
            Allocations = new[] { new { ChargeId = chargeId, Amount = amount } },
            CashRegisterId = registerId,
        });
    }
}
