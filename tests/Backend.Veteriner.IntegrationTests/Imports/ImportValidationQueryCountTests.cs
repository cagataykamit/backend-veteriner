using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Infrastructure.Persistence;
using ClosedXML.Excel;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Imports;

/// <summary>
/// İçe aktarma B3: 10.000 satırda doğrulama sorgularının N+1 olmadığı kanıtı. Aynı yapıdaki küçük ve büyük dosyanın
/// doğrulamasında (istek + arka plan işçisi, K13) çalışan SELECT sayısı eşit olmalıdır (satır sayısından bağımsız); satır
/// sonuçları EF toplu yazmasıyla (komut başına çok satır, parça parça) kaydedilir. Sayılar test çıktısına yazılır.
/// </summary>
[Collection("imports-api")]
public sealed class ImportValidationQueryCountTests
{
    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly ITestOutputHelper _output;

    public ImportValidationQueryCountTests(ITestOutputHelper output) => _output = output;

    // 3.400 x 3 tablo = 10.200 satır; 8.000 x 3 = 24.000 satır (parti toplam sınırı 25.000 altında).
    [Theory]
    [InlineData(3400)]
    [InlineData(8000)]
    public async Task Validation_Select_Count_Should_Not_Depend_On_Row_Count(int largePerTable)
    {
        var capture = new SqlCapture();
        using var baseFactory = new CustomWebApplicationFactory();
        using var factory = baseFactory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(capture))));
        var env = await SeedAsync(factory);

        var small = await RunAsync(factory, env, capture, perTable: 3);
        var large = await RunAsync(factory, env, capture, perTable: largePerTable);

        _output.WriteLine(
            $"KÜÇÜK  satır={small.Rows} süre={small.Elapsed.TotalMilliseconds:F0} ms komut={small.Commands} select={small.Selects} satır-güncelleme-komutu={small.RowUpdateCommands} diğer={small.Other}");
        _output.WriteLine(
            $"BÜYÜK  satır={large.Rows} süre={large.Elapsed.TotalMilliseconds:F0} ms komut={large.Commands} select={large.Selects} satır-güncelleme-komutu={large.RowUpdateCommands} diğer={large.Other}");
        _output.WriteLine(large.SelectDump);

        large.Rows.Should().BeGreaterThanOrEqualTo(10_000);
        large.Rows.Should().BeLessThanOrEqualTo(25_000);
        large.Status.Should().Be("Validated");
        small.Status.Should().Be("Validated");
        large.Selects.Should().Be(small.Selects, "SELECT sayısı satır sayısından bağımsız olmalı (N+1 yok)");
        large.Selects.Should().BeLessThanOrEqualTo(25);
        large.MaxRepeat.Should().BeLessThanOrEqualTo(3, "aynı SELECT metni en çok tablo sayısı kadar (başlık okuma) tekrarlanır, satır başına değil");
        large.RowUpdateCommands.Should().BeLessThan(large.Rows / 10, "satır sonuçları toplu yazılır, satır başına komut yok");
        large.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(120));
    }

    private sealed record Run(
        int Rows, TimeSpan Elapsed, string Status, int Commands, int Selects, int RowUpdateCommands, int Other, int MaxRepeat, string SelectDump);

    private sealed record Env(Guid TenantId, Guid ClinicId, string Token, Guid SpeciesId, string SpeciesName, string VaccineName, string Suffix);

    private async Task<Run> RunAsync(WebApplicationFactory<global::Program> factory, Env env, SqlCapture capture, int perTable)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", env.Token);
        client.Timeout = TimeSpan.FromMinutes(10);

        var bytes = BuildWorkbook(env, perTable);
        var form = new MultipartFormDataContent { { new StringContent(Guid.NewGuid().ToString()), "requestId" }, { new StringContent(env.ClinicId.ToString()), "clinicId" } };
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(XlsxType);
        form.Add(file, "file", "toplu.xlsx");
        var upload = await client.PostAsync("/api/v1/imports/batches", form);
        upload.StatusCode.Should().Be(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync());
        var id = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var map = await client.PutAsJsonAsync($"/api/v1/imports/batches/{id}/mapping", new
        {
            columns = new Dictionary<string, Dictionary<string, string>>
            {
                ["customers"] = Identity("MusteriNo", "AdSoyad", "Telefon", "Eposta"),
                ["pets"] = Identity("MusteriNo", "HayvanNo", "HayvanAdi", "Tur", "Mikrocip"),
                ["vaccinations"] = Identity("HayvanNo", "AsiAdi", "UygulamaTarihi", "SonrakiAsiTarihi"),
            },
        });
        map.StatusCode.Should().Be(HttpStatusCode.OK, await map.Content.ReadAsStringAsync());

        // İstek (kuyruğa alma) + arka plan işçisinin tüm turu birlikte sayılır (ADR-019 K13).
        capture.Clear();
        var watch = Stopwatch.StartNew();
        var response = await client.PostAsync($"/api/v1/imports/batches/{id}/validate", null);
        var requestElapsed = watch.Elapsed;
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        await ImportValidationTestRunner.RunWorkerAsync(factory.Services, id);
        watch.Stop();
        var summary = capture.Summarize();
        var json = await (await client.GetAsync($"/api/v1/imports/batches/{id}")).Content.ReadFromJsonAsync<JsonElement>();
        _output.WriteLine($"  perTable={perTable}: istek (202) {requestElapsed.TotalMilliseconds:F0} ms, istek + işçi {watch.Elapsed.TotalMilliseconds:F0} ms");

        // Önizleme ve satır sayfası da sabit sayıda sorgu çalıştırmalı.
        capture.Clear();
        (await client.GetAsync($"/api/v1/imports/batches/{id}/preview")).StatusCode.Should().Be(HttpStatusCode.OK);
        var previewCommands = capture.Summarize().Commands;
        capture.Clear();
        (await client.GetAsync($"/api/v1/imports/batches/{id}/rows?pageSize=200")).StatusCode.Should().Be(HttpStatusCode.OK);
        var rowsCommands = capture.Summarize().Commands;
        _output.WriteLine($"  perTable={perTable}: preview komut={previewCommands}, rows komut={rowsCommands}");
        previewCommands.Should().BeLessThanOrEqualTo(8);
        rowsCommands.Should().BeLessThanOrEqualTo(6);

        return new Run(
            perTable * 3,
            watch.Elapsed,
            json.GetProperty("status").GetString()!,
            summary.Commands,
            summary.Selects,
            summary.RowUpdateCommands,
            summary.Commands - summary.Selects - summary.RowUpdateCommands,
            summary.MaxSelectRepeat,
            summary.SelectDump);
    }

    private static Dictionary<string, string> Identity(params string[] fields) => fields.ToDictionary(f => f, f => f);

    /// <summary>
    /// perTable müşteri/hasta/aşı satırı. İlk müşteri-hasta-aşı üçlüsü kiracıdaki mevcut kayıtlarla eşleşir (üç mevcut-veri
    /// sorgusu da her çalıştırmada çalışsın). Tüm veriler sentetiktir.
    /// </summary>
    private static byte[] BuildWorkbook(Env env, int perTable)
    {
        var today = DateTime.UtcNow;
        using var workbook = new XLWorkbook();

        var customers = workbook.Worksheets.Add("Musteriler");
        Row(customers, 1, "MusteriNo", "AdSoyad", "Telefon", "Eposta");
        var pets = workbook.Worksheets.Add("Hayvanlar");
        Row(pets, 1, "MusteriNo", "HayvanNo", "HayvanAdi", "Tur", "Mikrocip");
        var vaccinations = workbook.Worksheets.Add("Asilar");
        Row(vaccinations, 1, "HayvanNo", "AsiAdi", "UygulamaTarihi", "SonrakiAsiTarihi");

        var run = Guid.NewGuid().ToString("N")[..6];
        for (var i = 0; i < perTable; i++)
        {
            var row = i + 2;
            var matchesExisting = i == 0;
            var name = matchesExisting ? $"Mevcut {env.Suffix}" : $"Müşteri {run} {i}";
            var phone = matchesExisting ? "05321230000" : $"0532{1_000_000 + i:D7}";
            Row(customers, row, $"M{i}", name, phone, i % 2 == 0 ? $"m{run}{i}@example.com" : null);
            Row(
                pets, row, $"M{i}", $"H{i}", matchesExisting ? "Boncuk" : $"Pet {i}", env.SpeciesName,
                matchesExisting ? null : $"985{i:D12}");
            Row(
                vaccinations, row, $"H{i}", env.VaccineName,
                matchesExisting ? "10.04.2024" : $"{1 + i % 27:D2}.{1 + i % 12:D2}.{2018 + i % 6}",
                i % 3 == 0 ? today.AddDays(1 + i % 20).ToString("dd.MM.yyyy") : null);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void Row(IXLWorksheet sheet, int row, params string?[] cells)
    {
        for (var c = 0; c < cells.Length; c++)
        {
            if (cells[c] is { } value)
                sheet.Cell(row, c + 1).SetValue(value);
        }
    }

    private static async Task<Env> SeedAsync(WebApplicationFactory<global::Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var tenant = new Tenant($"imq-{Guid.NewGuid():N}"[..18], DateTime.UtcNow);
        db.Tenants.Add(tenant);
        // Ice aktarma mekanigi testleri ucretli kiracida kosar; deneme kiracisinda tablo basina 500 satir siniri var (TRIAL-004).
        var subscription = TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14);
        subscription.ActivatePaidPlan(SubscriptionPlanCode.Basic, DateTime.UtcNow);
        db.TenantSubscriptions.Add(subscription);
        var clinic = new Clinic(tenant.Id, $"Imp-{Guid.NewGuid():N}"[..14], "Istanbul");
        db.Clinics.Add(clinic);

        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var species = new Species($"IMQ{suffix}", $"Imp Tür {suffix}");
        var vaccine = VaccineDefinition.CreateTenant(tenant.Id, $"IMQV{suffix}", $"Imp Aşı {suffix}", DateTime.UtcNow);
        db.Species.Add(species);
        db.VaccineDefinitions.Add(vaccine);

        var existing = new Client(tenant.Id, $"Mevcut {suffix}", DateTime.UtcNow, "905321230000", null, null);
        db.Clients.Add(existing);
        var existingPet = new Pet(tenant.Id, existing.Id, "Boncuk", species.Id);
        db.Pets.Add(existingPet);
        await db.SaveChangesAsync();
        db.Vaccinations.Add(new Vaccination(
            tenant.Id, existingPet.Id, clinic.Id, null, vaccine.Id, vaccine.Name, VaccinationStatus.Applied,
            new DateTime(2024, 4, 10, 9, 0, 0, DateTimeKind.Utc), null, null, DateTime.UtcNow));
        await db.SaveChangesAsync();

        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(factory.Services);
        var token = await IntegrationTestAuthHelper.SeedScopedClinicUserAndIssueTokenAsync(
            db, jwt, hasher, tenant.Id, clinic.Id, ["Imports.Read", "Imports.Manage"]);
        return new Env(tenant.Id, clinic.Id, token, species.Id, species.Name, vaccine.Name, suffix);
    }

    /// <summary>İstek başına EF komutlarını (metin) toplar.</summary>
    private sealed class SqlCapture : DbCommandInterceptor
    {
        private readonly object _gate = new();
        private readonly List<string> _commands = [];

        public void Clear()
        {
            lock (_gate) _commands.Clear();
        }

        public (int Commands, int Selects, int RowUpdateCommands, int MaxSelectRepeat, string SelectDump) Summarize()
        {
            lock (_gate)
            {
                var rowUpdates = _commands.Count(IsRowWrite);
                var selects = _commands.Where(c => !IsRowWrite(c) && c.Contains("SELECT", StringComparison.Ordinal)
                                                   && !c.Contains("INSERT", StringComparison.Ordinal)
                                                   && !c.Contains("UPDATE", StringComparison.Ordinal)).ToList();
                var repeat = selects.Count == 0 ? 0 : selects.GroupBy(x => x).Max(g => g.Count());
                var dump = string.Join(
                    "\n",
                    selects.GroupBy(x => x).Select(g => $"  x{g.Count()} {Shorten(g.Key)}"));
                return (_commands.Count, selects.Count, rowUpdates, repeat, dump);
            }
        }

        private static bool IsRowWrite(string text) => text.Contains("[ImportBatchRows]", StringComparison.Ordinal)
                                                      && text.Contains("UPDATE", StringComparison.Ordinal);

        private static string Shorten(string text)
        {
            var single = text.Replace("\r", " ").Replace("\n", " ");
            return single.Length <= 160 ? single : single[..160] + "…";
        }

        private void Add(DbCommand command)
        {
            lock (_gate) _commands.Add(command.CommandText);
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

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Add(command);
            return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
        }

        public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
        {
            Add(command);
            return base.NonQueryExecuted(command, eventData, result);
        }

        public override ValueTask<object?> ScalarExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
        {
            Add(command);
            return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
        }

        public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
        {
            Add(command);
            return base.ScalarExecuted(command, eventData, result);
        }
    }
}
