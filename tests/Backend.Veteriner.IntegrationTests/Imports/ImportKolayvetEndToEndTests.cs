using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Imports.Commit;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Imports;
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
/// Kolayvet liste dışa aktarmaları uçtan uca (IMPORT_API_CONTRACT.md §15; gerçek SQL Server/LocalDB): dört dosya tek alanda → dönüştür
/// → doğrula → uygula → doğum tarihi ve mikroçip raporlardan, devir borcu, sonraki aşı tarihi, saat 10:00 aşı randevusu → geri al.
/// Yapı gerçek dosyadan (1. satır sayfa başlığı); değerler uydurmadır.
/// </summary>
[Collection("imports-api")]
public sealed class ImportKolayvetEndToEndTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] Permissions =
    [
        "Imports.Read", "Imports.Manage", "Imports.Commit", "Clients.Create", "Clients.Read", "Pets.Create", "Pets.Read",
        "Vaccinations.Create", "Charges.Create", "Charges.Read", "Appointments.Create", "Appointments.Read",
    ];

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    private readonly CustomWebApplicationFactory _factory;

    public ImportKolayvetEndToEndTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Kolayvet_Exports_Should_Import_Owners_Pets_Vaccines_And_Roll_Back()
    {
        var ctx = await SeedAsync();
        var http = ClientFor(ctx.Token);
        var ayse = $"Ayşe {ctx.Suffix}";
        var can = $"Can {ctx.Suffix}";
        var today = OperationDayBounds.ToLocal(DateTime.UtcNow).Date;
        var due = today.AddDays(30);
        var planned = today.AddDays(40);

        var customers = Sheet("Müşteri Listesi | Kolayvet",
            ["Müşteri NO", "Müşteri Adı", "Cep Telefonu", "Kalan bakiye", "TC Kimlik No"],
            ["1001", ayse, "0532 111 22 330532 111 22 33 Hemen Ara WhatsApp'tan Yazın", "0", "10000000146"],
            ["1002", can, "0533 444 55 660533 444 55 66 Hemen Ara WhatsApp'tan Yazın", "150.5", null]);
        var pets = Sheet("Hasta Listesi | Kolayvet",
            ["Müşteri NO", "Müşteri Adı", "Hasta NO", "Hasta Adı", "Türü", "Cinsiyet", "Irkı", "Aşı Takvimi", "Mikroçip"],
            ["1001", ayse, "2001", "Pamuk", ctx.SpeciesName, "Dişi", null, "Oluşturuldu", null],
            ["1002", can, "2002", "Duman", ctx.SpeciesName, "Erkek", null, "Oluşturuldu", null]);
        var vaccines = Sheet("Aşı Takvimi | Kolayvet",
            ["Müşteri NO", "Müşteri Adı", "Hasta NO", "Hasta Adı", "Aşı", "Aşı Tarihi", "Yapılma durumu", "Yapılma Tarihi"],
            ["1001", ayse, "2001", "Pamuk", ctx.VaccineName, "08 Şubat 2025", "Yapıldı", "2025-02-10"],
            ["1001", ayse, "2001", "Pamuk", ctx.VaccineName, due.ToString("dd MMMM yyyy", Tr), "Yapılmadı", "null"],
            ["1002", can, "2002", "Duman", ctx.VaccineName, planned.ToString("dd MMMM yyyy", Tr), "Yapılmadı", "null"]);
        var report = Sheet("Yeni Hasta Listesi | Kolayvet",
            ["Müşteri Bilgileri", "Hasta Bilgileri", "Ek Bilgiler"],
            [$"{ayse}\r\nTel: 0532 111 22 33T.C.: 10000000146", $"PAMUK\r\n{ctx.SpeciesName} / Tekir  Dişi",
                "Yaş: 3 yaşındaDoğum tarihi: 12 Temmuz 2023Mikroçip No: 985000000000001Tarbil Durumu: KayıtlıKayıt tarihi: 01 Mayıs 2025"]);

        var id = await UploadAsync(http, ctx, customers, pets, vaccines, report);
        var validationId = (await ImportValidationTestRunner.ValidateAsync(_factory.Services, http, id)).GetProperty("validationId").GetGuid();
        var preview = await GetJsonAsync(http, $"/api/v1/imports/batches/{id}/preview");
        preview.GetProperty("canCommit").GetBoolean().Should().BeTrue(preview.ToString());

        var commit = await http.PostAsJsonAsync($"/api/v1/imports/batches/{id}/commit", new { validationId });
        commit.StatusCode.Should().Be(HttpStatusCode.Accepted, await commit.Content.ReadAsStringAsync());
        await RunCommitWorkerAsync();

        var batch = await GetJsonAsync(http, $"/api/v1/imports/batches/{id}");
        batch.GetProperty("status").GetString().Should().Be("Committed", batch.ToString());
        foreach (var (table, created) in new[] { ("customers", 2), ("pets", 2), ("vaccinations", 1), ("appointments", 1) })
            batch.GetProperty("summary").GetProperty(table).GetProperty("created").GetInt32().Should().Be(created, table);

        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clients = await db.Clients.AsNoTracking().Where(c => c.TenantId == ctx.TenantId).ToListAsync();
            clients.Single(c => c.FullName == ayse).NationalId.Should().Be("10000000146");
            clients.Select(c => c.Phone).Should().BeEquivalentTo("905321112233", "905334445566");
            (await db.Charges.AsNoTracking().SingleAsync(c => c.ImportBatchId == id)).Amount.Should().Be(150.50m, "kalan bakiye devir borcudur");

            var allPets = await db.Pets.AsNoTracking().Where(p => p.TenantId == ctx.TenantId).ToListAsync();
            var pamuk = allPets.Single(p => p.Name == "Pamuk");
            (pamuk.BirthDate, pamuk.MicrochipNumber).Should().Be((new DateOnly(2023, 7, 12), "985000000000001"), "detaylı rapordan");
            pamuk.Notes.Should().Contain("Eski programa kayıt: 01.05.2025");
            var duman = allPets.Single(p => p.Name == "Duman");
            duman.BirthDate.Should().BeNull();

            var vaccination = await db.Vaccinations.AsNoTracking().SingleAsync(v => v.ImportBatchId == id);
            vaccination.PetId.Should().Be(pamuk.Id);
            OperationDayBounds.ToLocalDate(vaccination.DueAtUtc!.Value).Should().Be(DateOnly.FromDateTime(due), "yapılmamış aşı sonraki aşı tarihidir");

            var appointment = await db.Appointments.AsNoTracking().SingleAsync(a => a.ImportBatchId == id);
            appointment.PetId.Should().Be(duman.Id);
            OperationDayBounds.ToLocal(appointment.ScheduledAtUtc).Should().Be(planned.AddHours(10), "saatsiz aşı randevusu 10:00");
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
        }
    }

    private static IOptions<ImportCommitOptions> WorkerOptions()
        => Options.Create(new ImportCommitOptions { Backpressure = new ImportBackpressureOptions { Enabled = false } });

    private async Task RunCommitWorkerAsync()
    {
        var processor = new ImportCommitProcessor(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(), WorkerOptions(), TimeProvider.System,
            NullLogger<ImportCommitProcessor>.Instance, $"w-kv-{Guid.NewGuid():N}"[..20]);
        for (var i = 0; i < 20 && await processor.ProcessNextAsync(CancellationToken.None); i++)
        {
        }
    }

    private async Task RunRollbackWorkerAsync()
    {
        var processor = new ImportRollbackProcessor(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(), WorkerOptions(), TimeProvider.System,
            NullLogger<ImportRollbackProcessor>.Instance, $"w-kr-{Guid.NewGuid():N}"[..20]);
        for (var i = 0; i < 20 && await processor.ProcessNextAsync(CancellationToken.None); i++)
        {
        }
    }

    private static async Task<Guid> UploadAsync(HttpClient http, SeedCtx ctx, params byte[][] files)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(Guid.NewGuid().ToString()), "requestId" },
            { new StringContent(ctx.ClinicId.ToString()), "clinicId" },
            { new StringContent("Kolayvet"), "sourceProfile" },
        };
        for (var i = 0; i < files.Length; i++)
        {
            var content = new ByteArrayContent(files[i]);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            form.Add(content, "kolayvetFiles", $"kolayvet-{i}.xlsx");
        }

        var response = await http.PostAsync("/api/v1/imports/batches", form);
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        var batch = await response.Content.ReadFromJsonAsync<JsonElement>();
        batch.GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString())
            .Should().Contain("Imports.Kolayvet.PatientReport");
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

    /// <summary>Kolayvet dışa aktarması: 1. satır sayfa başlığı, 2. satır sütun adları.</summary>
    private static byte[] Sheet(string title, string?[] header, params string?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Sheet1");
        sheet.Cell(1, 1).SetValue(title);
        foreach (var (row, r) in new[] { header }.Concat(rows).Select((row, r) => (row, r)))
        {
            for (var c = 0; c < row.Length; c++)
            {
                if (row[c] is { } s)
                    sheet.Cell(r + 2, c + 1).SetValue(s);
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

        var tenant = new Tenant($"ikv-{Guid.NewGuid():N}"[..18], DateTime.UtcNow);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        var clinic = new Clinic(tenant.Id, $"Ikv-{Guid.NewGuid():N}"[..14], "Istanbul");
        db.Clinics.Add(clinic);
        var species = new Species($"IKV{suffix}", $"Ikv Tür {suffix}");
        db.Species.Add(species);
        var vaccine = VaccineDefinition.CreateTenant(tenant.Id, $"IKVV{suffix}", $"Ikv Aşı {suffix}", DateTime.UtcNow);
        db.VaccineDefinitions.Add(vaccine);
        await db.SaveChangesAsync();

        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);
        var token = await IntegrationTestAuthHelper.SeedScopedClinicUserAndIssueTokenAsync(db, jwt, hasher, tenant.Id, clinic.Id, Permissions);
        return new SeedCtx(tenant.Id, clinic.Id, token, suffix, species.Name, vaccine.Name);
    }

    private sealed record SeedCtx(Guid TenantId, Guid ClinicId, string Token, string Suffix, string SpeciesName, string VaccineName);
}
