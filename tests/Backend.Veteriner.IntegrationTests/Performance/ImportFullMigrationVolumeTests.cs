using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Imports;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Imports.Commit;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Imports;
using Backend.Veteriner.Infrastructure.Persistence;
using ClosedXML.Excel;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Performance;

/// <summary>
/// Tam geçiş hacim ölçümü (ADR-048; docs/IMPORT_PERFORMANCE_FINDINGS.md §9). Yalnız <c>VETINITY_PERF=1</c> iken çalışır.
/// Yedi tablolu sentetik dosya: varsayılan 3.000 müşteri, 5.000 hasta, 8.000 klinik geçmiş, 6.000 hesap hareketi, 1.500
/// randevu, 1.000 ürün (24.500 satır; parti sınırı 25.000, tablo sınırı 10.000). Aşama süreleri ve doğrulamada SELECT
/// sayısının satır sayısından bağımsızlığı (küçük dosyayla karşılaştırma) ölçülür; uygulama ve geri alma gerçek işçiyle.
/// Ölçek <c>IMPORT_FM_SCALE</c> (varsayılan 1,0) ile çarpılır. İzole entegrasyon test veritabanında çalışır.
/// </summary>
[Collection("imports-api")]
public sealed class ImportFullMigrationVolumeTests
{
    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static readonly string[] Permissions =
    [
        "Imports.Read", "Imports.Manage", "Imports.Commit", "Clients.Create", "Pets.Create", "Vaccinations.Create",
        "Examinations.Create", "Charges.Create", "Appointments.Create", "Products.Create",
    ];

    private readonly ITestOutputHelper _output;

    public ImportFullMigrationVolumeTests(ITestOutputHelper output) => _output = output;

    private sealed record Volume(int Customers, int Pets, int Records, int Entries, int Appointments, int Products)
    {
        public int Total => Customers + Pets + Records + Entries + Appointments + Products;

        public static Volume Scaled(double factor) => new(
            (int)(3000 * factor), (int)(5000 * factor), (int)(8000 * factor), (int)(6000 * factor), (int)(1500 * factor), (int)(1000 * factor));
    }

    [PerfFact]
    public async Task Full_Migration_Volume_Should_Validate_Commit_And_Roll_Back_With_Row_Independent_Selects()
    {
        var factor = double.TryParse(Environment.GetEnvironmentVariable("IMPORT_FM_SCALE"), System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 1.0;
        var capture = new CommandCounter();
        using var baseFactory = new CustomWebApplicationFactory();
        using var factory = baseFactory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(capture))));
        var env = await SeedAsync(factory);
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", env.Token);
        http.Timeout = TimeSpan.FromMinutes(20);

        // Küçük dosya: SELECT taban çizgisi.
        var small = await UploadAndValidateAsync(factory, http, env, capture, new Volume(3, 5, 8, 6, 2, 2), "kucuk");
        var volume = Volume.Scaled(factor);
        var large = await UploadAndValidateAsync(factory, http, env, capture, volume, "buyuk");

        _output.WriteLine($"Satır: {volume.Total} (müşteri {volume.Customers}, hasta {volume.Pets}, klinik geçmiş {volume.Records}, hareket {volume.Entries}, randevu {volume.Appointments}, ürün {volume.Products})");
        _output.WriteLine($"Yükleme: {large.Upload.TotalSeconds:F1} sn; doğrulama (istek + işçi): {large.Validate.TotalSeconds:F1} sn; SELECT küçük/büyük: {small.Selects}/{large.Selects}");
        large.Status.Should().Be("Validated");
        large.Selects.Should().Be(small.Selects, "doğrulama SELECT sayısı satır sayısından bağımsız (N+1 yok)");

        var commitWatch = Stopwatch.StartNew();
        var commit = await http.PostAsJsonAsync($"/api/v1/imports/batches/{large.Id}/commit", new { validationId = large.ValidationId });
        commit.StatusCode.Should().Be(HttpStatusCode.Accepted, await commit.Content.ReadAsStringAsync());
        await RunAsync(new ImportCommitProcessor(
            factory.Services.GetRequiredService<IServiceScopeFactory>(), NoBackpressure(), TimeProvider.System,
            NullLogger<ImportCommitProcessor>.Instance, $"w-vol-{Guid.NewGuid():N}"[..20]).ProcessNextAsync);
        commitWatch.Stop();

        var batch = await GetJsonAsync(http, $"/api/v1/imports/batches/{large.Id}");
        batch.GetProperty("status").GetString().Should().Be("Committed");
        var summary = batch.GetProperty("summary");
        string Created(string table) => $"{table}={summary.GetProperty(table).GetProperty("created").GetInt32()}/{summary.GetProperty(table).GetProperty("failed").GetInt32()}";
        _output.WriteLine($"Uygulama: {commitWatch.Elapsed.TotalSeconds:F1} sn; oluşan/başarısız: {Created("customers")} {Created("pets")} {Created("clinicalRecords")} {Created("accountEntries")} {Created("appointments")} {Created("products")}");
        foreach (var table in new[] { "customers", "pets", "clinicalRecords", "accountEntries", "appointments", "products" })
            summary.GetProperty(table).GetProperty("failed").GetInt32().Should().Be(0, table);

        var previewWatch = Stopwatch.StartNew();
        var rollbackPreview = await GetJsonAsync(http, $"/api/v1/imports/batches/{large.Id}/rollback-preview");
        previewWatch.Stop();
        rollbackPreview.GetProperty("canRollback").GetBoolean().Should().BeTrue(rollbackPreview.ToString());

        var rollbackWatch = Stopwatch.StartNew();
        var rollback = await http.PostAsJsonAsync($"/api/v1/imports/batches/{large.Id}/rollback",
            new { requestId = Guid.NewGuid(), reason = "Hacim ölçümü", blockedPolicy = "Fail" });
        rollback.StatusCode.Should().Be(HttpStatusCode.Accepted, await rollback.Content.ReadAsStringAsync());
        await RunAsync(new ImportRollbackProcessor(
            factory.Services.GetRequiredService<IServiceScopeFactory>(), NoBackpressure(), TimeProvider.System,
            NullLogger<ImportRollbackProcessor>.Instance, $"w-vrb-{Guid.NewGuid():N}"[..20]).ProcessNextAsync);
        rollbackWatch.Stop();

        (await GetJsonAsync(http, $"/api/v1/imports/batches/{large.Id}")).GetProperty("status").GetString().Should().Be("RolledBack");
        _output.WriteLine($"Geri alma önizleme: {previewWatch.Elapsed.TotalSeconds:F1} sn; geri alma: {rollbackWatch.Elapsed.TotalSeconds:F1} sn");

        await using var scope = factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.ImportedClinicalRecords.CountAsync(r => r.TenantId == env.TenantId)).Should().Be(0, "küçük parti yalnız doğrulandı, büyük parti geri alındı");
        (await db.Charges.CountAsync(c => c.TenantId == env.TenantId && c.ImportBatchId == large.Id)).Should().Be(0);
    }

    private sealed record Run(Guid Id, Guid ValidationId, string Status, TimeSpan Upload, TimeSpan Validate, int Selects, int Records);

    private static IOptions<ImportCommitOptions> NoBackpressure()
        => Options.Create(new ImportCommitOptions { Backpressure = new ImportBackpressureOptions { Enabled = false } });

    private static async Task RunAsync(Func<CancellationToken, Task<bool>> next)
    {
        for (var i = 0; i < 20 && await next(CancellationToken.None); i++)
        {
        }
    }

    private async Task<Run> UploadAndValidateAsync(
        WebApplicationFactory<global::Program> factory, HttpClient http, Env env, CommandCounter capture, Volume volume, string label)
    {
        var bytes = BuildWorkbook(env, volume);
        var watch = Stopwatch.StartNew();
        var form = new MultipartFormDataContent
        {
            { new StringContent(Guid.NewGuid().ToString()), "requestId" },
            { new StringContent(env.ClinicId.ToString()), "clinicId" },
        };
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(XlsxType);
        form.Add(file, "file", $"{label}.xlsx");
        var upload = await http.PostAsync("/api/v1/imports/batches", form);
        upload.StatusCode.Should().Be(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync());
        var body = await upload.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();
        var uploadElapsed = watch.Elapsed;

        var columns = body.GetProperty("tables").EnumerateArray().ToDictionary(
            t => t.GetProperty("table").GetString()!,
            t => t.GetProperty("columns").EnumerateArray()
                .Where(c => c.GetProperty("suggested").ValueKind == JsonValueKind.String)
                .ToDictionary(c => c.GetProperty("suggested").GetString()!, c => c.GetProperty("source").GetString()!));
        (await http.PutAsJsonAsync($"/api/v1/imports/batches/{id}/mapping", new { columns })).StatusCode.Should().Be(HttpStatusCode.OK);

        capture.Clear();
        watch.Restart();
        var validated = await ImportValidationTestRunner.ValidateAsync(factory.Services, http, id);
        var validateElapsed = watch.Elapsed;
        var selects = capture.Selects;
        _output.WriteLine($"  {label}: yükleme {uploadElapsed.TotalMilliseconds:F0} ms, doğrulama {validateElapsed.TotalMilliseconds:F0} ms, select={selects}");

        return new Run(
            id,
            validated.GetProperty("validationId").ValueKind == JsonValueKind.String ? validated.GetProperty("validationId").GetGuid() : Guid.Empty,
            validated.GetProperty("status").GetString()!,
            uploadElapsed,
            validateElapsed,
            selects,
            volume.Records);
    }

    /// <summary>Sentetik yedi tablolu dosya; şablon başlıkları (öneri Exact). Gerçek kişisel veri yok.</summary>
    private static byte[] BuildWorkbook(Env env, Volume v)
    {
        var run = Guid.NewGuid().ToString("N")[..6];
        var soon = OperationDayBounds.ToLocal(DateTime.UtcNow).Date.AddDays(3);
        using var workbook = new XLWorkbook();

        var customers = Sheet(workbook, "Musteriler", "MusteriNo", "AdSoyad", "Telefon", "Eposta", "AcikBakiye");
        for (var i = 0; i < v.Customers; i++)
            Row(customers, i + 2, $"M{i}", $"Müşteri {run} {i}", $"0532{1_000_000 + i:D7}", i % 2 == 0 ? $"m{run}{i}@example.com" : null, i % 4 == 0 ? $"{100 + i},50" : null);

        var pets = Sheet(workbook, "Hastalar", "MusteriNo", "HastaNo", "HastaAdi", "Tur");
        for (var i = 0; i < v.Pets; i++)
            Row(pets, i + 2, $"M{i % Math.Max(1, v.Customers)}", $"H{i}", $"Pet {i}", env.SpeciesName);

        var records = Sheet(workbook, "KlinikGecmis", "HastaNo", "Tarih", "KayitTuru", "Sikayet", "Tani", "Tedavi", "Hekim");
        for (var i = 0; i < v.Records; i++)
            Row(records, i + 2, $"H{i % Math.Max(1, v.Pets)}", $"{1 + i % 27:D2}.{1 + i % 12:D2}.{2016 + i % 9}", "Muayene", $"Şikâyet {i}", $"Tanı {i}", "Tedavi metni", "Dr. Eski");

        var entries = Sheet(workbook, "HesapHareketleri", "MusteriNo", "Tarih", "IslemTuru", "Tutar", "BelgeNo");
        for (var i = 0; i < v.Entries; i++)
            Row(entries, i + 2, $"M{i % Math.Max(1, v.Customers)}", $"{1 + i % 27:D2}.{1 + i % 12:D2}.{2019 + i % 6}", i % 2 == 0 ? "Borç" : "Tahsilat", $"{50 + i % 900},00", $"B-{i}");

        var appointments = Sheet(workbook, "Randevular", "HastaNo", "Tarih", "Saat", "RandevuTuru");
        for (var i = 0; i < v.Appointments; i++)
            Row(appointments, i + 2, $"H{i % Math.Max(1, v.Pets)}", soon.AddDays(i % 60).ToString("dd.MM.yyyy"), $"{9 + i % 9:D2}:{(i % 4) * 15:D2}", "Kontrol");

        var products = Sheet(workbook, "Urunler", "Ad", "UrunTuru", "SatisFiyati", "Barkod");
        for (var i = 0; i < v.Products; i++)
            Row(products, i + 2, $"Ürün {run} {i}", i % 3 == 0 ? "Hizmet" : "Diğer", $"{10 + i}", i % 3 == 0 ? null : $"869{run}{i:D6}");

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static IXLWorksheet Sheet(XLWorkbook workbook, string name, params string[] headers)
    {
        var sheet = workbook.Worksheets.Add(name);
        Row(sheet, 1, headers);
        return sheet;
    }

    private static void Row(IXLWorksheet sheet, int row, params string?[] cells)
    {
        for (var c = 0; c < cells.Length; c++)
        {
            if (cells[c] is { } value)
                sheet.Cell(row, c + 1).SetValue(value);
        }
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient http, string url)
    {
        var response = await http.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private sealed record Env(Guid TenantId, Guid ClinicId, string Token, string SpeciesName);

    private static async Task<Env> SeedAsync(WebApplicationFactory<global::Program> factory)
    {
        await using var scope = factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var tenant = new Tenant($"imv-{Guid.NewGuid():N}"[..18], DateTime.UtcNow);
        db.Tenants.Add(tenant);
        // Deneme kiracısında tablo başına 500 satır sınırı var (TRIAL-004); ölçüm ücretli kiracıda.
        var subscription = TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14);
        subscription.ActivatePaidPlan(SubscriptionPlanCode.Basic, DateTime.UtcNow);
        db.TenantSubscriptions.Add(subscription);
        var clinic = new Clinic(tenant.Id, $"Imv-{Guid.NewGuid():N}"[..14], "Istanbul");
        db.Clinics.Add(clinic);
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var species = new Species($"IMV{suffix}", $"Imv Tür {suffix}");
        db.Species.Add(species);
        await db.SaveChangesAsync();

        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(factory.Services);
        var token = await IntegrationTestAuthHelper.SeedScopedClinicUserAndIssueTokenAsync(db, jwt, hasher, tenant.Id, clinic.Id, Permissions);
        return new Env(tenant.Id, clinic.Id, token, species.Name);
    }

    /// <summary>Okuma komutlarını sayar (SELECT; satır sonucu yazımı hariç).</summary>
    private sealed class CommandCounter : DbCommandInterceptor
    {
        private int _selects;

        public int Selects => Volatile.Read(ref _selects);

        public void Clear() => Interlocked.Exchange(ref _selects, 0);

        private void Add(DbCommand command)
        {
            var text = command.CommandText;
            if (text.Contains("SELECT", StringComparison.Ordinal)
                && !text.Contains("INSERT", StringComparison.Ordinal)
                && !text.Contains("UPDATE", StringComparison.Ordinal)
                && !text.Contains("DELETE", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _selects);
            }
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            Add(command);
            return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }

        public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
        {
            Add(command);
            return base.ReaderExecuted(command, eventData, result);
        }
    }
}
