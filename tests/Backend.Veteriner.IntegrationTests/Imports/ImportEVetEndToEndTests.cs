using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Imports.Commit;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Imports;
using Backend.Veteriner.Infrastructure.Persistence;
using ClosedXML.Excel;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Imports;

/// <summary>
/// E-vet "Genel &gt; Yedek Al" yüklemesi uçtan uca (IMPORT_API_CONTRACT.md §13; gerçek SQL Server/LocalDB): dönüştürme → doğrula →
/// uygula → fatura kimliği, pasif müşteri, alerji, vefat (geçmiş kayıtlardan sonra), sonraki aşı tarihi, yalnız kilolu muayene →
/// geri al. Yapı gerçek dosyadan; değerler uydurmadır.
/// </summary>
[Collection("imports-api")]
public sealed class ImportEVetEndToEndTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] Permissions =
    [
        "Imports.Read", "Imports.Manage", "Imports.Commit", "Clients.Create", "Clients.Read", "Pets.Create", "Pets.Read",
        "Vaccinations.Create", "Examinations.Create", "Examinations.Read", "Charges.Create", "Charges.Read",
        "Appointments.Create", "Appointments.Read",
    ];

    private readonly CustomWebApplicationFactory _factory;

    public ImportEVetEndToEndTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task EVet_Backup_Should_Import_Identity_Status_History_And_Roll_Back()
    {
        var ctx = await SeedAsync();
        var http = ClientFor(ctx.Token);
        var owner = $"Ayşe {ctx.Suffix}";
        var company = $"Örnek Ltd {ctx.Suffix}";
        var today = OperationDayBounds.ToLocal(DateTime.UtcNow);
        var due = today.Date.AddDays(30).AddHours(10);
        var checkup = today.Date.AddDays(40).AddHours(14);

        var bytes = Workbook(
            ("Müşteri Listesi", [
                ["Kayıt Tarihi", "Müşteriler", "Bakiye", "Protokol No", "Kimlik No", "Gsm", "Vergi Dairesi", "Vergi No", "Durum"],
                ["2024-01-01", owner, "1250.5000001", "101", "10000000146", "905321234567", null, null, "Aktif"],
                ["2024-01-01", company, "0", "102", null, "905331234567", "Kadıköy", "1234567890", "Pasif"]]),
            ("Hasta Listesi", [
                ["Müşteriler", "Hastalar", "Protokol No", "Hasta Türü", "Cinsiyet", "Ağırlık", "Alerjik Durumu", "Ölüm Zamanı", "Durum"],
                [owner, "Pamuk", "201", ctx.SpeciesName, "Kısırlaştırılmış Dişi", "4.2", "Penisilin", null, "Aktif"],
                [owner, "Duman", "202", ctx.SpeciesName, "Erkek", null, null, "2025-09-02", "Ölü"]]),
            ("Randevu Listesi", [
                ["Müşteriler", "Hastalar", "Görev Tipi", "Başlangıç Tarihi", "Aşı Paketi", "Veteriner", "Durum"],
                [owner, "Duman", "Aşılama", "2025-01-10T10:00:00", ctx.VaccineName, "Dr. A", "Tamamlandı"],
                [owner, "Pamuk", "Tamamlandı", "2025-02-10T10:00:00", ctx.VaccineName, "Dr. A", "Tamamlandı"],
                [owner, "Pamuk", "Aşılama", due.ToString("yyyy-MM-ddTHH:mm:ss"), ctx.VaccineName, "Dr. A", "Yapılacak"],
                [owner, "Pamuk", "Kontrol Muayanesi", checkup.ToString("yyyy-MM-ddTHH:mm:ss"), null, "Dr. A", "Yapılacak"],
                [null, null, "Maaş", "2025-03-01T09:00:00", null, "Dr. A", "Tamamlandı"]]),
            ("Muayene Listesi", [
                ["Tarih", "Hasta Sahibi", "Hasta Adı", "Veteriner Hekim", "Ağırlık", "Semptom"],
                ["2025-06-01T11:00:00", owner, "Pamuk", "Dr. A", "4.3", null],
                ["2025-06-02T11:00:00", owner, "Duman", "Dr. A", "0", "Kusma"]]));

        var id = await UploadAsync(http, ctx, bytes);
        var validationId = (await ImportValidationTestRunner.ValidateAsync(_factory.Services, http, id)).GetProperty("validationId").GetGuid();
        var preview = await GetJsonAsync(http, $"/api/v1/imports/batches/{id}/preview");
        preview.GetProperty("canCommit").GetBoolean().Should().BeTrue(preview.ToString());

        var commit = await http.PostAsJsonAsync($"/api/v1/imports/batches/{id}/commit", new { validationId });
        commit.StatusCode.Should().Be(HttpStatusCode.Accepted, await commit.Content.ReadAsStringAsync());
        await RunCommitWorkerAsync();

        var batch = await GetJsonAsync(http, $"/api/v1/imports/batches/{id}");
        batch.GetProperty("status").GetString().Should().Be("Committed", batch.ToString());
        foreach (var (table, created) in new[] { ("customers", 2), ("pets", 2), ("vaccinations", 2), ("clinicalRecords", 2), ("appointments", 1) })
            batch.GetProperty("summary").GetProperty(table).GetProperty("created").GetInt32().Should().Be(created, table);

        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clients = await db.Clients.AsNoTracking().Where(c => c.TenantId == ctx.TenantId).ToListAsync();
            var person = clients.Single(c => c.FullName == owner);
            (person.PersonType, person.NationalId, person.RemindersOptedOut).Should().Be((ClientPersonType.Individual, "10000000146", false));
            var corporate = clients.Single(c => c.FullName == company);
            (corporate.PersonType, corporate.TaxNumber, corporate.TaxOffice, corporate.LegalName, corporate.RemindersOptedOut)
                .Should().Be((ClientPersonType.Corporate, "1234567890", "Kadıköy", company, true));
            (await db.Charges.AsNoTracking().SingleAsync(c => c.ImportBatchId == id)).Amount.Should().Be(1250.50m);

            var pets = await db.Pets.AsNoTracking().Where(p => p.TenantId == ctx.TenantId).ToListAsync();
            var pamuk = pets.Single(p => p.Name == "Pamuk");
            (pamuk.Gender, pamuk.IsNeutered, pamuk.AlertFlags, pamuk.AlertNote, pamuk.Status).Should()
                .Be((PetGender.Female, true, PetAlertFlags.Allergy, "Penisilin", PetStatus.Active));
            var duman = pets.Single(p => p.Name == "Duman");
            (duman.Status, duman.DeceasedOn).Should().Be((PetStatus.Deceased, new DateOnly(2025, 9, 2)));

            var vaccinations = await db.Vaccinations.AsNoTracking().Where(v => v.ImportBatchId == id).ToListAsync();
            vaccinations.Should().Contain(v => v.PetId == duman.Id, "vefat geçmiş kayıtlardan sonra yazılır");
            OperationDayBounds.ToLocalDate(vaccinations.Single(v => v.PetId == pamuk.Id).DueAtUtc!.Value)
                .Should().Be(DateOnly.FromDateTime(due), "yapılacak aşı görevi sonraki aşı tarihidir");
            (await db.ImportedClinicalRecords.AsNoTracking().Where(r => r.ImportBatchId == id).Select(r => r.WeightKg).ToListAsync())
                .Should().Contain(4.3m, "yalnız kilo taşıyan muayene aktarılır");
            (await db.Appointments.AsNoTracking().SingleAsync(a => a.ImportBatchId == id)).PetId.Should().Be(pamuk.Id);
        }

        var rollback = await http.PostAsJsonAsync($"/api/v1/imports/batches/{id}/rollback",
            new { requestId = Guid.NewGuid(), reason = "Deneme", blockedPolicy = "Fail" });
        rollback.StatusCode.Should().Be(HttpStatusCode.Accepted, await rollback.Content.ReadAsStringAsync());
        await RunRollbackWorkerAsync();

        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == id)).Status.Should().Be(ImportBatchStatus.RolledBack);
            (await db.Clients.CountAsync(c => c.TenantId == ctx.TenantId)).Should().Be(0);
            (await db.Pets.CountAsync(p => p.TenantId == ctx.TenantId)).Should().Be(0);
            (await db.ImportedClinicalRecords.CountAsync(r => r.ImportBatchId == id)).Should().Be(0);
        }
    }

    [Fact]
    public async Task EVet_Reports_Should_Be_Rejected_Without_EVet_Profile()
    {
        var ctx = await SeedAsync();
        var http = ClientFor(ctx.Token);
        var form = new MultipartFormDataContent
        {
            { new StringContent(Guid.NewGuid().ToString()), "requestId" },
            { new StringContent(ctx.ClinicId.ToString()), "clinicId" },
        };
        form.Add(Xlsx(Workbook(("Müşteri Listesi", [["Müşteriler"], ["Ayşe"]]))), "file", "yedek.xlsx");
        form.Add(new ByteArrayContent("a;b\n"u8.ToArray()) { Headers = { ContentType = new MediaTypeHeaderValue("text/csv") } }, "evetPatientReportFile", "hasta.csv");

        var response = await http.PostAsync("/api/v1/imports/batches", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Imports.UnsupportedFile");
    }

    // ================================================================ Yardımcılar

    private static IOptions<ImportCommitOptions> WorkerOptions()
        => Options.Create(new ImportCommitOptions { Backpressure = new ImportBackpressureOptions { Enabled = false } });

    private async Task RunCommitWorkerAsync()
    {
        var processor = new ImportCommitProcessor(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(), WorkerOptions(), TimeProvider.System,
            NullLogger<ImportCommitProcessor>.Instance, $"w-ev-{Guid.NewGuid():N}"[..20]);
        for (var i = 0; i < 20 && await processor.ProcessNextAsync(CancellationToken.None); i++)
        {
        }
    }

    private async Task RunRollbackWorkerAsync()
    {
        var processor = new ImportRollbackProcessor(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(), WorkerOptions(), TimeProvider.System,
            NullLogger<ImportRollbackProcessor>.Instance, $"w-er-{Guid.NewGuid():N}"[..20]);
        for (var i = 0; i < 20 && await processor.ProcessNextAsync(CancellationToken.None); i++)
        {
        }
    }

    private static ByteArrayContent Xlsx(byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        return content;
    }

    private static async Task<Guid> UploadAsync(HttpClient http, SeedCtx ctx, byte[] bytes)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(Guid.NewGuid().ToString()), "requestId" },
            { new StringContent(ctx.ClinicId.ToString()), "clinicId" },
            { new StringContent("EVet"), "sourceProfile" },
        };
        form.Add(Xlsx(bytes), "file", "backup.xlsx");

        var response = await http.PostAsync("/api/v1/imports/batches", form);
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        var batch = await response.Content.ReadFromJsonAsync<JsonElement>();
        batch.GetProperty("warnings").EnumerateArray().Should().Contain(w => w.GetProperty("code").GetString() == "Imports.EVetConversion");
        var id = batch.GetProperty("id").GetGuid();

        // Dönüştürülmüş başlıklar kanonik: önerilen eşleme kaydedilir.
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

    /// <summary>Kiracı + klinik + teste özgü tür ve aşı tanımı + yetkili kullanıcı.</summary>
    private async Task<SeedCtx> SeedAsync()
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var tenant = new Tenant($"iev-{Guid.NewGuid():N}"[..18], DateTime.UtcNow);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        var clinic = new Clinic(tenant.Id, $"Iev-{Guid.NewGuid():N}"[..14], "Istanbul");
        db.Clinics.Add(clinic);
        var species = new Species($"IEV{suffix}", $"Iev Tür {suffix}");
        db.Species.Add(species);
        var vaccine = VaccineDefinition.CreateTenant(tenant.Id, $"IEVV{suffix}", $"Iev Aşı {suffix}", DateTime.UtcNow);
        db.VaccineDefinitions.Add(vaccine);
        await db.SaveChangesAsync();

        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);
        var token = await IntegrationTestAuthHelper.SeedScopedClinicUserAndIssueTokenAsync(db, jwt, hasher, tenant.Id, clinic.Id, Permissions);
        return new SeedCtx(tenant.Id, clinic.Id, token, suffix, species.Name, vaccine.Name);
    }

    private sealed record SeedCtx(Guid TenantId, Guid ClinicId, string Token, string Suffix, string SpeciesName, string VaccineName);
}
