using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Imports.Commit;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Reminders;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Imports;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Reminders;
using ClosedXML.Excel;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Imports;

/// <summary>
/// Tam geçiş uçtan uca (ADR-048, IMPORT_API_CONTRACT.md §12; gerçek SQL Server/LocalDB): yedi tablolu dosya yükle →
/// doğrula → uygula → okuma uçları → hatırlatma dışlaması/serbest bırakma → geri al. Devir ücretinin bakiyede ve borç
/// raporunda göründüğü, gün sonu cirosunda görünmediği; parti dışı kaydın geri almayı engellediği doğrulanır. Sentetik veri.
/// </summary>
[Collection("imports-api")]
public sealed class ImportFullMigrationEndToEndTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] Permissions =
    [
        "Imports.Read", "Imports.Manage", "Imports.Commit", "Clients.Create", "Clients.Read", "Pets.Create", "Pets.Read",
        "Vaccinations.Create", "Examinations.Create", "Examinations.Read", "Charges.Create", "Charges.Read", "Payments.Read",
        "Appointments.Create", "Appointments.Read", "Products.Create", "Products.Read",
    ];

    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly CustomWebApplicationFactory _factory;

    public ImportFullMigrationEndToEndTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Full_Migration_Should_Apply_Read_Isolate_Finance_Gate_Reminders_And_Roll_Back()
    {
        var ctx = await SeedAsync();
        var http = ClientFor(ctx.Token);
        var sfx = ctx.Suffix;
        var soon = OperationDayBounds.ToLocal(DateTime.UtcNow.AddHours(3));

        var bytes = Workbook(
            ("Musteriler", [
                ["MusteriNo", "AdSoyad", "Telefon", "Eposta", "AcikBakiye", "BakiyeTarihi"],
                ["M1", $"Ayşe {sfx}", "05321234567", $"ayse.{sfx.ToLowerInvariant()}@example.test", "1.250,50", "31.10.2026"],
                ["M2", $"Veli {sfx}", "05331234567", null, null, null]]),
            ("Hastalar", [
                ["MusteriNo", "HastaNo", "HastaAdi", "Tur"],
                ["M1", "H1", "Pamuk", ctx.SpeciesName],
                ["M2", "H2", "Kehribar", ctx.SpeciesName]]),
            ("KlinikGecmis", [
                ["HastaNo", "Tarih", "KayitTuru", "Sikayet", "Tani", "Recete", "Hekim"],
                ["H1", "12.05.2024", "Muayene", "Kusma", "Gastroenterit", "Metronidazol 2x1", "Dr. Eski"],
                ["H2", "03.02.2025", "Kontrol", "Kaşıntı", null, null, null]]),
            ("HesapHareketleri", [
                ["MusteriNo", "Tarih", "IslemTuru", "Tutar", "Aciklama", "BelgeNo"],
                ["M1", "05.03.2025", "Borç", "500", "Muayene", "F-1"],
                ["M1", "06.03.2025", "Tahsilat", "350", "Nakit", "T-1"]]),
            ("Randevular", [
                ["HastaNo", "Tarih", "Saat", "RandevuTuru"],
                ["H1", soon.ToString("dd.MM.yyyy"), soon.ToString("HH:mm"), "Kontrol"]]),
            ("Urunler", [
                ["Ad", "UrunTuru", "SatisFiyati", "Barkod"],
                [$"Mama {sfx}", "Diğer", "100", $"869{sfx}1"],
                [$"Muayene {sfx}", "Hizmet", "300", null]]));

        var id = await UploadAsync(http, ctx, bytes);
        var validationId = (await ImportValidationTestRunner.ValidateAsync(_factory.Services, http, id)).GetProperty("validationId").GetGuid();

        var preview = await GetJsonAsync(http, $"/api/v1/imports/batches/{id}/preview");
        preview.GetProperty("canCommit").GetBoolean().Should().BeTrue(preview.ToString());

        var commit = await http.PostAsJsonAsync($"/api/v1/imports/batches/{id}/commit", new { validationId });
        commit.StatusCode.Should().Be(HttpStatusCode.Accepted, await commit.Content.ReadAsStringAsync());
        await RunCommitWorkerAsync();

        var batch = await GetJsonAsync(http, $"/api/v1/imports/batches/{id}");
        batch.GetProperty("status").GetString().Should().Be("Committed");
        foreach (var (table, created) in new[] { ("customers", 2), ("pets", 2), ("clinicalRecords", 2), ("accountEntries", 2), ("appointments", 1), ("products", 2) })
            batch.GetProperty("summary").GetProperty(table).GetProperty("created").GetInt32().Should().Be(created, table);

        Guid m1, m2, h1;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rows = await db.ImportBatchRows.AsNoTracking().Where(r => r.BatchId == id).ToListAsync();
            rows.Should().NotContain(r => r.ApplyStatus == ImportRowApplyStatus.Pending || r.ApplyStatus == ImportRowApplyStatus.Failed);
            m1 = rows.Single(r => r.Table == ImportTable.Customers && r.RowNumber == 2).TargetEntityId!.Value;
            m2 = rows.Single(r => r.Table == ImportTable.Customers && r.RowNumber == 3).TargetEntityId!.Value;
            h1 = rows.Single(r => r.Table == ImportTable.Pets && r.RowNumber == 2).TargetEntityId!.Value;

            var devir = await db.Charges.AsNoTracking().SingleAsync(c => c.TenantId == ctx.TenantId && c.ImportBatchId == id);
            devir.ClientId.Should().Be(m1);
            devir.Amount.Should().Be(1250.50m);
            devir.Description.Should().Be("Devir bakiyesi (eski program, 31.10.2026)");
            devir.PetId.Should().BeNull();
            devir.CreatedByUserId.Should().NotBe(Guid.Empty);

            (await db.ImportedClinicalRecords.CountAsync(r => r.ImportBatchId == id)).Should().Be(2);
            (await db.ImportedAccountEntries.CountAsync(e => e.ImportBatchId == id)).Should().Be(2);
            var appointment = await db.Appointments.AsNoTracking().SingleAsync(a => a.TenantId == ctx.TenantId);
            appointment.ImportBatchId.Should().Be(id);
            appointment.ClinicId.Should().Be(ctx.ClinicId);
            (await db.Products.CountAsync(p => p.TenantId == ctx.TenantId)).Should().Be(2);
            (await db.Prescriptions.CountAsync(p => p.TenantId == ctx.TenantId)).Should().Be(0, "reçete metin olarak gelir (T2)");
            (await db.Examinations.CountAsync(e => e.TenantId == ctx.TenantId)).Should().Be(0, "muayene defterine girmez (T1)");
            (await db.Payments.CountAsync(p => p.TenantId == ctx.TenantId)).Should().Be(0, "tahsilat geçmişi ödeme yazmaz (T4)");
        }

        // Okuma uçları (§12.8).
        var records = await GetJsonAsync(http, $"/api/v1/pets/{h1}/imported-clinical-records");
        records.GetProperty("totalItems").GetInt32().Should().Be(1);
        var record = records.GetProperty("items")[0];
        record.GetProperty("diagnosis").GetString().Should().Be("Gastroenterit");
        record.GetProperty("prescription").GetString().Should().Be("Metronidazol 2x1");
        record.GetProperty("recordedOn").GetString().Should().Be("2024-05-12");

        var entries = await GetJsonAsync(http, $"/api/v1/clients/{m1}/imported-account-entries");
        entries.GetProperty("totalItems").GetInt32().Should().Be(2);
        entries.GetProperty("totals").GetProperty("debit").GetDecimal().Should().Be(500m);
        entries.GetProperty("totals").GetProperty("credit").GetDecimal().Should().Be(350m);
        entries.GetProperty("items")[0].GetProperty("kind").GetString().Should().Be("Credit", "yeniden eskiye");

        var timeline = await GetJsonAsync(http, $"/api/v1/pets/{h1}/timeline");
        var imported = timeline.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("type").GetString() == "ImportedClinicalRecord");
        imported.GetProperty("title").GetString().Should().Be("Muayene");
        imported.GetProperty("linkRef").ValueKind.Should().Be(JsonValueKind.Null);

        // Finans yalıtımı (§12.7): bakiye ve borç raporu devri içerir, gün sonu cirosu içermez.
        var balance = await GetJsonAsync(http, $"/api/v1/clients/{m1}/balance");
        balance.GetProperty("outstandingAmount").GetDecimal().Should().Be(1250.50m);
        balance.ToString().Should().Contain("\"isImported\":true");

        var outstanding = await GetJsonAsync(http, $"/api/v1/reports/outstanding-balances?clinicId={ctx.ClinicId}");
        outstanding.GetProperty("totalOutstandingAmount").GetDecimal().Should().Be(1250.50m);

        var today = OperationDayBounds.ToLocalDate(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var daily = await GetJsonAsync(http, $"/api/v1/reports/daily-transactions?from={today}&to={today}&clinicId={ctx.ClinicId}&pageSize=200");
        daily.GetProperty("totals").GetProperty("grossChargeAmount").GetDecimal().Should().Be(0m, "devir eski dönemin cirosudur");
        daily.GetProperty("items").GetArrayLength().Should().Be(0);

        // Hatırlatma (§12.6): içe aktarılan randevu serbest bırakılana kadar aday değildir.
        await RunReminderProcessorAsync();
        (await AppointmentDispatchCountAsync(ctx.TenantId)).Should().Be(0);

        var impact = (await GetJsonAsync(http, $"/api/v1/imports/batches/{id}/preview")).GetProperty("reminderImpact");
        impact.GetProperty("futureAppointments").GetInt32().Should().Be(1);
        impact.GetProperty("appointmentEmailsWithinReminderWindow").GetInt32().Should().Be(1);
        var total = impact.GetProperty("totalEmailsWithinReminderWindow").GetInt32();

        var wrong = await http.PostAsJsonAsync($"/api/v1/imports/batches/{id}/release-reminders", new { confirmEmailCount = total + 7 });
        wrong.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await wrong.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totalEmailsWithinReminderWindow").GetInt32().Should().Be(total);

        var released = await http.PostAsJsonAsync($"/api/v1/imports/batches/{id}/release-reminders", new { confirmEmailCount = total });
        released.StatusCode.Should().Be(HttpStatusCode.OK, await released.Content.ReadAsStringAsync());
        await RunReminderProcessorAsync();
        (await AppointmentDispatchCountAsync(ctx.TenantId)).Should().Be(1, "serbest bırakılınca mevcut kuralla değerlendirilir");

        // Geri alma (§12.9): parti dışı ücret M2'yi korur; Fail politikası hiçbir şey silmez.
        await AddManualChargeAsync(ctx, m2);
        var rollbackPreview = await GetJsonAsync(http, $"/api/v1/imports/batches/{id}/rollback-preview");
        rollbackPreview.GetProperty("canRollback").GetBoolean().Should().BeFalse();
        rollbackPreview.GetProperty("deletable").GetProperty("openingBalances").GetInt32().Should().Be(1);
        rollbackPreview.GetProperty("deletable").GetProperty("clinicalRecords").GetInt32().Should().Be(2);
        rollbackPreview.GetProperty("deletable").GetProperty("products").GetInt32().Should().Be(2);
        rollbackPreview.GetProperty("blocked").GetProperty("clients").GetInt32().Should().Be(1);
        rollbackPreview.GetProperty("blockedItems").EnumerateArray()
            .Should().ContainSingle(i => i.GetProperty("id").GetGuid() == m2)
            .Which.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()).Should().Contain("HasCharges");

        var blocked = await http.PostAsJsonAsync($"/api/v1/imports/batches/{id}/rollback",
            new { requestId = Guid.NewGuid(), reason = "Deneme", blockedPolicy = "Fail" });
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var partial = await http.PostAsJsonAsync($"/api/v1/imports/batches/{id}/rollback",
            new { requestId = Guid.NewGuid(), reason = "Yanlış dosya", blockedPolicy = "DeleteUnblockedOnly" });
        partial.StatusCode.Should().Be(HttpStatusCode.Accepted, await partial.Content.ReadAsStringAsync());
        await RunRollbackWorkerAsync();

        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == id)).Status.Should().Be(ImportBatchStatus.PartiallyRolledBack);
            (await db.Clients.AsNoTracking().Where(c => c.TenantId == ctx.TenantId).Select(c => c.Id).ToListAsync()).Should().Equal(m2);
            (await db.Charges.CountAsync(c => c.TenantId == ctx.TenantId && c.ImportBatchId == id)).Should().Be(0, "devir müşteriyle silindi");
            (await db.ImportedClinicalRecords.CountAsync(r => r.ImportBatchId == id)).Should().Be(0);
            (await db.ImportedAccountEntries.CountAsync(e => e.ImportBatchId == id)).Should().Be(0);
            (await db.Appointments.CountAsync(a => a.TenantId == ctx.TenantId)).Should().Be(0);
            (await db.Products.CountAsync(p => p.TenantId == ctx.TenantId)).Should().Be(0);
            (await db.OutboxMessages.AsNoTracking()
                    .Where(m => m.Type == AppointmentIntegrationEventTypes.Deleted && m.CreatedAtUtc > DateTime.UtcNow.AddMinutes(-5))
                    .Select(m => m.Payload).ToListAsync())
                .Should().Contain(p => p.Contains(ctx.ClinicId.ToString(), StringComparison.OrdinalIgnoreCase));
        }
    }

    // ================================================================ Yardımcılar

    private async Task AddManualChargeAsync(SeedCtx ctx, Guid clientId)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var charge = Charge.Create(ctx.TenantId, ctx.ClinicId, clientId, null, null, null, "Muayene", 1, 100m, Guid.NewGuid(), DateTime.UtcNow, null);
        db.Charges.Add(charge.Value!);
        await db.SaveChangesAsync();
    }

    private async Task<int> AppointmentDispatchCountAsync(Guid tenantId)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().ReminderDispatchLogs.AsNoTracking()
            .CountAsync(l => l.TenantId == tenantId && l.SourceEntityType == ReminderSourceEntityType.Appointment);
    }

    private async Task RunReminderProcessorAsync()
    {
        await using var scope = _factory.Services.CreateSystemScope();
        await scope.ServiceProvider.GetRequiredService<ReminderProcessorService>().ProcessOnceAsync(CancellationToken.None);
    }

    private async Task RunCommitWorkerAsync()
    {
        var processor = new ImportCommitProcessor(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ImportCommitOptions { Backpressure = new ImportBackpressureOptions { Enabled = false } }),
            TimeProvider.System,
            NullLogger<ImportCommitProcessor>.Instance,
            $"w-fm-{Guid.NewGuid():N}"[..20]);
        for (var i = 0; i < 20 && await processor.ProcessNextAsync(CancellationToken.None); i++)
        {
        }
    }

    private async Task RunRollbackWorkerAsync()
    {
        var processor = new ImportRollbackProcessor(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ImportCommitOptions { Backpressure = new ImportBackpressureOptions { Enabled = false } }),
            TimeProvider.System,
            NullLogger<ImportRollbackProcessor>.Instance,
            $"w-fr-{Guid.NewGuid():N}"[..20]);
        for (var i = 0; i < 20 && await processor.ProcessNextAsync(CancellationToken.None); i++)
        {
        }
    }

    private static async Task<Guid> UploadAsync(HttpClient http, SeedCtx ctx, byte[] bytes)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(Guid.NewGuid().ToString()), "requestId" },
            { new StringContent(ctx.ClinicId.ToString()), "clinicId" },
        };
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(XlsxType);
        form.Add(content, "file", "tam-gecis.xlsx");

        var response = await http.PostAsync("/api/v1/imports/batches", form);
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        var batch = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = batch.GetProperty("id").GetGuid();

        // Şablon başlıkları kanonik: önerilen eşleme kaydedilir.
        var columns = batch.GetProperty("tables").EnumerateArray().ToDictionary(
            t => t.GetProperty("table").GetString()!,
            t => t.GetProperty("columns").EnumerateArray()
                .Where(c => c.GetProperty("suggested").ValueKind == JsonValueKind.String)
                .ToDictionary(c => c.GetProperty("suggested").GetString()!, c => c.GetProperty("source").GetString()!));
        var mapped = await http.PutAsJsonAsync($"/api/v1/imports/batches/{id}/mapping", new { columns, valueMappings = Array.Empty<object>() });
        mapped.StatusCode.Should().Be(HttpStatusCode.OK, await mapped.Content.ReadAsStringAsync());
        return id;
    }

    private static byte[] Workbook(params (string Sheet, object?[][] Rows)[] sheets)
    {
        using var workbook = new XLWorkbook();
        foreach (var (name, rows) in sheets)
        {
            var sheet = workbook.Worksheets.Add(name);
            for (var r = 0; r < rows.Length; r++)
            {
                for (var c = 0; c < rows[r].Length; c++)
                {
                    if (rows[r][c] is string s)
                        sheet.Cell(r + 1, c + 1).SetValue(s);
                }
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient http, string url)
    {
        var response = await http.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"{url}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private HttpClient ClientFor(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Kiracı + klinik + teste özgü tür + randevu ve aşı hatırlatması açık + tam yetkili kullanıcı.</summary>
    private async Task<SeedCtx> SeedAsync()
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var tenant = new Tenant($"imf-{Guid.NewGuid():N}"[..18], DateTime.UtcNow);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        var clinic = new Clinic(tenant.Id, $"Imf-{Guid.NewGuid():N}"[..14], "Istanbul");
        db.Clinics.Add(clinic);
        var species = new Species($"IMF{suffix}", $"Imf Tür {suffix}");
        db.Species.Add(species);
        var settings = TenantReminderSettings.CreateDefault(tenant.Id, DateTime.UtcNow);
        settings.Update(true, 24, true, 3, true, DateTime.UtcNow);
        db.TenantReminderSettings.Add(settings);
        await db.SaveChangesAsync();

        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);
        var token = await IntegrationTestAuthHelper.SeedScopedClinicUserAndIssueTokenAsync(db, jwt, hasher, tenant.Id, clinic.Id, Permissions);
        return new SeedCtx(tenant.Id, clinic.Id, token, suffix, species.Name);
    }

    private sealed record SeedCtx(Guid TenantId, Guid ClinicId, string Token, string Suffix, string SpeciesName);
}
