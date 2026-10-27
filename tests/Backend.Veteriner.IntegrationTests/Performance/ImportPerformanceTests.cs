using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Imports.Commit;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Reminders;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Domain.Visits;
using Backend.Veteriner.Infrastructure.Imports;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Projections.Clients;
using Backend.Veteriner.Infrastructure.Reminders;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Performance;

/// <summary>
/// Dilim T: içe aktarma uçtan uca deneme ve ölçüm (docs/IMPORT_PERFORMANCE_FINDINGS.md). Yalnız <c>VETINITY_PERF=1</c> ile
/// çalışır; izole, koşu kimlikli LocalDB test veritabanı (paylaşılan/dev DB'ye dokunmaz). Gerçek HTTP yığını
/// (WebApplicationFactory, JWT, yetki, EF, SQL). Doğrulama, uygulama ve geri alma işleri üretimdeki
/// <see cref="ImportValidationHostedService"/> / <see cref="ImportCommitHostedService"/> / <see cref="ImportRollbackHostedService"/> sınıflarıyla, kısa döngüyle
/// (<c>PollIntervalSeconds = 1</c>) ve varsayılan geri basınçla gerçekten arka planda çalışır; müşteri projeksiyonu ve
/// genel outbox işleyicisi üretim döngüleriyle açıktır (e-posta no-op). Her aşamada süre, EF komut sayısı (kaynağa göre:
/// HTTP isteği / işçi / diğer arka plan), ayrılan bellek ve GC sayıları ölçülür; R4 için <c>/health/ready</c> ve outbox
/// bekleyenleri her saniye örneklenir. Sonuç markdown olarak <c>PERF_OUT</c> yoluna yazılır (aşama aşama).
/// </summary>
public sealed class ImportPerformanceTests
{
    private static readonly string[] Permissions =
    [
        "Imports.Read", "Imports.Manage", "Imports.Commit", "Clients.Create", "Pets.Create", "Vaccinations.Create",
    ];

    private readonly ITestOutputHelper _output;

    public ImportPerformanceTests(ITestOutputHelper output) => _output = output;

    [PerfFact]
    public async Task Import_End_To_End_At_Pilot_Volume()
    {
        var scale = ImportPilotScale.FromEnvironment();
        var outPath = Environment.GetEnvironmentVariable("PERF_OUT") ?? Path.Combine(Path.GetTempPath(), "import-perf.md");
        var capture = new SqlBucketCapture();
        using var baseFactory = new CustomWebApplicationFactory();
        using var factory = baseFactory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(capture));
            // Üretim benzeri tüketiciler: müşteri projeksiyonu 2 sn / 50, genel outbox 5 sn; e-posta gönderilmez.
            s.RemoveAll<IEmailSenderImmediate>();
            s.AddScoped<IEmailSenderImmediate, NoOpEmailSenderImmediate>();
            s.PostConfigure<OutboxOptions>(o => o.LoopIntervalSeconds = 5);
            s.PostConfigure<ClientProjectionOptions>(o =>
            {
                o.Enabled = true;
                o.LoopIntervalSeconds = 2;
                o.BatchSize = 50;
            });
        }));

        var report = new Report(outPath);
        var checks = new List<(string Name, bool Ok, string Detail)>();
        void Check(string name, bool ok, string detail = "") => checks.Add((name, ok, detail));

        var today = OperationDayBounds.ToLocalDate(DateTime.UtcNow);
        var genWatch = Stopwatch.StartNew();
        var gen = new ImportPilotFileGenerator(scale, today);
        var xlsxA = gen.BatchAXlsx();
        var csvA = gen.BatchACsv1254();
        var csvB = gen.BatchBVaccinationsCsv1254();
        genWatch.Stop();

        report.Line($"# İçe aktarma deneme/perf ham çıktısı ({DateTime.Now:yyyy-MM-dd HH:mm})");
        report.Line();
        report.Line($"Ölçek: {scale}. A partisi: müşteri {gen.Customers.Count} + hasta {gen.Pets.Count} + aşı {gen.SplitIndex} (XLSX {xlsxA.Length / 1024} KB); " +
                    $"B partisi: aşı {gen.Vaccinations.Count - gen.SplitIndex} (CSV Windows-1254 {csvB.Length / 1024} KB). " +
                    $"CSV varyantı (A): {(csvA.Customers.Length + csvA.Pets.Length + csvA.Vaccinations.Length) / 1024} KB. Üretim {genWatch.ElapsedMilliseconds} ms.");
        report.Line($"Makine: {Environment.ProcessorCount} mantıksal işlemci, GC toplam kullanılabilir bellek {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024} MB, " +
                    $"sunucu GC={System.Runtime.GCSettings.IsServerGC}.");
        report.Line();
        report.Line("Enjekte edilen kirli veri: " + string.Join(", ", gen.Injected.Select(kv => $"{kv.Key}={kv.Value}")));
        report.Flush();

        // ---------------------------------------------------------------- Kiracı ve mevcut sistem verisi
        var ctx = await SeedTenantAsync(factory.Services, gen);
        using var http = factory.CreateClient();
        http.Timeout = TimeSpan.FromMinutes(30);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);

        var meter = new PhaseMeter(capture);
        using var sampler = new HealthSampler(factory);
        sampler.Phase = "Başlangıç";
        sampler.Start();
        await Task.Delay(1500);

        var commitLog = new ListLogger<ImportCommitProcessor>();
        var rollbackLog = new ListLogger<ImportRollbackProcessor>();
        var workerOptions = new ImportCommitOptions { WorkerEnabled = true, PollIntervalSeconds = 1 };
        var scopes = factory.Services.GetRequiredService<IServiceScopeFactory>();
        var commitWorker = new ImportCommitHostedService(
            new ImportCommitProcessor(scopes, Options.Create(workerOptions), TimeProvider.System, commitLog, ImportCommitProcessor.NewWorkerId()),
            Options.Create(workerOptions), TimeProvider.System, NullLogger<ImportCommitHostedService>.Instance);
        var rollbackWorker = new ImportRollbackHostedService(
            new ImportRollbackProcessor(scopes, Options.Create(workerOptions), TimeProvider.System, rollbackLog, ImportCommitProcessor.NewWorkerId()),
            Options.Create(workerOptions), TimeProvider.System, NullLogger<ImportRollbackHostedService>.Instance);
        var validationWorker = new ImportValidationHostedService(
            new ImportValidationProcessor(scopes, TimeProvider.System, NullLogger<ImportValidationProcessor>.Instance, ImportCommitProcessor.NewWorkerId()),
            Options.Create(workerOptions), TimeProvider.System, NullLogger<ImportValidationHostedService>.Instance);
        await StartTaggedAsync(validationWorker, SqlBucketCapture.Worker);
        await StartTaggedAsync(commitWorker, SqlBucketCapture.Worker);
        await StartTaggedAsync(rollbackWorker, SqlBucketCapture.Worker);

        try
        {
            // ------------------------------------------------------------ Tavan denemesi (tek dosya)
            if (gen.Vaccinations.Count > 10_000)
            {
                var whole = gen.BatchAXlsx(allVaccinations: true);
                var tooMany = await meter.RunAsync("Tek dosya (tüm aşılar) yükle → sınır", async () =>
                {
                    using var r = await PostXlsxAsync(http, whole, ctx.ClinicId);
                    return ((int)r.StatusCode, await CodeAsync(r));
                });
                meter.Note($"HTTP {tooMany.Item1} {tooMany.Item2}; dosya {whole.Length / 1024} KB");
                Check("Tek dosya 10.000 aşı satırı sınırını aşınca 400 Imports.TooManyRows", tooMany is (400, "Imports.TooManyRows"), $"{tooMany}");
            }

            // ------------------------------------------------------------ Şablon
            var template = await meter.RunAsync("Şablon indir (xlsx)", async () =>
            {
                using var r = await http.GetAsync("/api/v1/imports/template?format=xlsx");
                return ((int)r.StatusCode, (await r.Content.ReadAsByteArrayAsync()).Length);
            });
            meter.Note($"HTTP {template.Item1}, {template.Item2} bayt");

            // ------------------------------------------------------------ A: yükle → eşle → doğrula → önizleme
            sampler.Phase = "A hazırlık";
            var uploadA = await meter.RunAsync("A yükle (XLSX)", async () => await ExpectJsonAsync(await PostXlsxAsync(http, xlsxA, ctx.ClinicId)));
            var idA = uploadA.GetProperty("id").GetGuid();
            meter.Note(SuggestionNote(uploadA));
            var columnsA = SuggestedColumns(uploadA);
            Check("A: tüm dolu başlıklar için kanonik öneri (takma ad)", UnsuggestedColumns(uploadA) is [] or ["Notlar"], string.Join(",", UnsuggestedColumns(uploadA)));

            await meter.RunAsync("A eşle (PUT mapping)", async () =>
                await ExpectJsonAsync(await http.PutAsJsonAsync($"/api/v1/imports/batches/{idA}/mapping", new { columns = columnsA, valueMappings = Array.Empty<object>() })));

            var validated1 = await ValidateAsync(meter, http, idA, "A doğrula (1. tur)");
            meter.Note($"durum {validated1.GetProperty("status").GetString()}; {SummaryNote(validated1)}");

            var preview1 = await meter.RunAsync("A önizleme (1. tur)", async () => await GetJsonAsync(http, $"/api/v1/imports/batches/{idA}/preview"));
            var valueMappingsA = SkipMappings(preview1);
            meter.Note($"canCommit={preview1.GetProperty("canCommit").GetBoolean()}, blockers=[{string.Join(",", preview1.GetProperty("blockers").EnumerateArray().Select(x => x.GetString()))}], eşlenemeyen: {UnmappedNote(preview1)}");
            Check("A 1. tur: bilinmeyen tür/aşı commit'i engeller (K6)", !preview1.GetProperty("canCommit").GetBoolean());

            await meter.RunAsync("A değer eşleme (atla) + yeniden eşle", async () =>
                await ExpectJsonAsync(await http.PutAsJsonAsync($"/api/v1/imports/batches/{idA}/mapping", new { columns = columnsA, valueMappings = valueMappingsA })));
            var validated2 = await ValidateAsync(meter, http, idA, "A doğrula (2. tur)");
            meter.Note($"durum {validated2.GetProperty("status").GetString()}; {SummaryNote(validated2)}");
            var validationIdA = validated2.GetProperty("validationId").GetGuid();
            CheckSummaryInvariants(validated2, "A doğrulama", Check, applied: false);

            var preview2 = await meter.RunAsync("A önizleme (2. tur)", async () => await GetJsonAsync(http, $"/api/v1/imports/batches/{idA}/preview"));
            meter.Note($"canCommit={preview2.GetProperty("canCommit").GetBoolean()}; reminderImpact {preview2.GetProperty("reminderImpact")}");
            Check("A 2. tur: commit edilebilir", preview2.GetProperty("canCommit").GetBoolean());
            report.Line();
            report.Line("A doğrulama sorun dağılımı (kod/önem/satır): " + IssueNote(preview2));
            report.Flush();

            var rowsPage = await meter.RunAsync("A satırlar (hatalı, 1. sayfa 200)", async () => await GetJsonAsync(http, $"/api/v1/imports/batches/{idA}/rows?status=Error&pageSize=200"));
            meter.Note($"toplam hatalı satır {rowsPage.GetProperty("totalItems").GetInt32()}");
            var errorsFile = await meter.RunAsync("A hata raporu indir (xlsx)", async () =>
            {
                using var r = await http.GetAsync($"/api/v1/imports/batches/{idA}/errors?format=xlsx");
                return ((int)r.StatusCode, (await r.Content.ReadAsByteArrayAsync()).Length);
            });
            meter.Note($"HTTP {errorsFile.Item1}, {errorsFile.Item2 / 1024} KB");

            // ------------------------------------------------------------ CSV (Windows-1254) varyantı: aynı sonuç mu?
            var uploadCsv = await meter.RunAsync("A CSV-1254 varyantı yükle", async () => await ExpectJsonAsync(await PostCsvAsync(http, ctx.ClinicId, csvA.Customers, csvA.Pets, csvA.Vaccinations)));
            var idCsv = uploadCsv.GetProperty("id").GetGuid();
            meter.Note($"uyarılar: [{string.Join(",", uploadCsv.GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString()))}]; {SuggestionNote(uploadCsv)}");
            (await http.PutAsJsonAsync($"/api/v1/imports/batches/{idCsv}/mapping", new { columns = SuggestedColumns(uploadCsv), valueMappings = valueMappingsA })).EnsureSuccessStatusCode();
            var validatedCsv = await ValidateAsync(meter, http, idCsv, "A CSV-1254 varyantı doğrula");
            meter.Note(SummaryNote(validatedCsv));
            Check("CSV-1254 ve XLSX varyantı aynı doğrulama sayıları", SummaryKey(validatedCsv) == SummaryKey(validated2),
                $"xlsx: {SummaryKey(validated2)} | csv: {SummaryKey(validatedCsv)}");
            (await http.DeleteAsync($"/api/v1/imports/batches/{idCsv}")).StatusCode.Should204(Check, "CSV varyantı taslağı silindi");

            // ------------------------------------------------------------ A: commit (arka plan) → ilerleme → sonuç
            var planA = await PlanAsync(factory.Services, idA);
            sampler.Phase = "A commit";
            var commitA = await meter.RunAsync("A commit + arka plan uygulama (yoklama 2 sn)", async () =>
            {
                using var r = await http.PostAsJsonAsync($"/api/v1/imports/batches/{idA}/commit", new { validationId = validationIdA, onErrors = "SkipErrorRows" });
                var status = (int)r.StatusCode;
                return (status, wait: await WaitWhileAsync(http, idA, "Committing", TimeSpan.FromMinutes(90)));
            });
            meter.Note($"POST {commitA.status} ilerleme: {commitA.wait}; sahip birimi {planA.Units.Count}, atlanan satır {planA.RowsToSkip.Count}; " +
                       $"işçi SQL/birim {Ratio(meter.Last.Commands.GetValueOrDefault(SqlBucketCapture.Worker), planA.Units.Count)}; geri basınç: {commitLog.Summary()}");
            Check("A commit: 202 ve Committed", commitA.status == 202 && commitA.wait.Final == "Committed", $"{commitA}");
            sampler.Phase = "A sonrası";

            var batchA = await meter.RunAsync("A sonuç (parti ayrıntısı)", async () => await GetJsonAsync(http, $"/api/v1/imports/batches/{idA}"));
            meter.Note(AppliedNote(batchA));
            CheckSummaryInvariants(batchA, "A uygulama", Check, applied: true);
            var dbCountsA = await TenantCountsAsync(factory.Services, ctx.TenantId);
            report.Line();
            report.Line($"A sonrası kiracı sayıları (müşteri/hasta/aşı, mevcut veri dahil): {dbCountsA}");

            // ------------------------------------------------------------ Hatırlatma dışlaması (K3)
            var previewAfterA = await GetJsonAsync(http, $"/api/v1/imports/batches/{idA}/preview");
            var impact = previewAfterA.GetProperty("reminderImpact");
            var emailsInWindow = impact.GetProperty("emailsWithinReminderWindow").GetInt32();
            var importedInWindow = await ImportedDueInWindowAsync(factory.Services, idA);
            await meter.RunAsync("Hatırlatma işçisi turu (serbest bırakmadan önce)", async () => await RunRemindersAsync(factory.Services));
            var dispatchBefore = await DispatchesAsync(factory.Services, ctx.TenantId, idA);
            meter.Note($"pencerede içe aktarılmış aşı {importedInWindow} (e-postalı {emailsInWindow}); içe aktarılan için gönderim günlüğü {dispatchBefore.Imported}; kontrol aşısı günlüğü {dispatchBefore.Control}");
            Check("K3: serbest bırakılmadan içe aktarılan aşıya hatırlatma üretilmedi", dispatchBefore.Imported == 0, $"{dispatchBefore}");
            Check("K3 kontrol: elle girilmiş pencere içi aşıya hatırlatma üretildi (işçi çalışıyor)", dispatchBefore.Control >= 1, $"{dispatchBefore}");
            Check("Pencerede içe aktarılmış aşı var (deneme anlamlı)", importedInWindow > 0, $"{importedInWindow}");

            // ------------------------------------------------------------ B: hasta adı + sahip ile mevcut hastalara bağlanan aşılar
            sampler.Phase = "B hazırlık";
            var uploadB = await meter.RunAsync("B yükle (CSV-1254, yalnız aşı)", async () => await ExpectJsonAsync(await PostCsvAsync(http, ctx.ClinicId, null, null, csvB)));
            var idB = uploadB.GetProperty("id").GetGuid();
            meter.Note(SuggestionNote(uploadB));
            var columnsB = SuggestedColumns(uploadB);
            (await http.PutAsJsonAsync($"/api/v1/imports/batches/{idB}/mapping", new { columns = columnsB, valueMappings = Array.Empty<object>() })).EnsureSuccessStatusCode();
            await ValidateAsync(meter, http, idB, "B doğrula (1. tur)");
            var previewB1 = await GetJsonAsync(http, $"/api/v1/imports/batches/{idB}/preview");
            (await http.PutAsJsonAsync($"/api/v1/imports/batches/{idB}/mapping", new { columns = columnsB, valueMappings = SkipMappings(previewB1) })).EnsureSuccessStatusCode();
            var validatedB = await ValidateAsync(meter, http, idB, "B doğrula (2. tur)");
            meter.Note($"{SummaryNote(validatedB)}");
            var previewB = await GetJsonAsync(http, $"/api/v1/imports/batches/{idB}/preview");
            report.Line();
            report.Line("B doğrulama sorun dağılımı: " + IssueNote(previewB));
            report.Flush();

            var planB = await PlanAsync(factory.Services, idB);
            sampler.Phase = "B commit";
            var commitB = await meter.RunAsync("B commit + arka plan uygulama", async () =>
            {
                using var r = await http.PostAsJsonAsync($"/api/v1/imports/batches/{idB}/commit", new { validationId = validatedB.GetProperty("validationId").GetGuid() });
                var status = (int)r.StatusCode;
                return (status, wait: await WaitWhileAsync(http, idB, "Committing", TimeSpan.FromMinutes(60)));
            });
            meter.Note($"POST {commitB.status} → {commitB.wait}; sahip birimi {planB.Units.Count}; işçi SQL/birim {Ratio(meter.Last.Commands.GetValueOrDefault(SqlBucketCapture.Worker), planB.Units.Count)}");
            Check("B commit: Committed", commitB.wait.Final == "Committed", $"{commitB}");
            sampler.Phase = "B sonrası";
            var batchB = await GetJsonAsync(http, $"/api/v1/imports/batches/{idB}");
            report.Line();
            report.Line($"B uygulama: {AppliedNote(batchB)}");
            CheckSummaryInvariants(batchB, "B uygulama", Check, applied: true);

            // ------------------------------------------------------------ Hatırlatma serbest bırakma
            var released = await meter.RunAsync("A hatırlatmaları serbest bırak", async () =>
                await ExpectJsonAsync(await http.PostAsJsonAsync($"/api/v1/imports/batches/{idA}/release-reminders", new { confirmEmailCount = emailsInWindow })));
            meter.Note($"alreadyReleased={released.GetProperty("alreadyReleased").GetBoolean()}, note={released.GetProperty("note")}");
            await meter.RunAsync("Hatırlatma işçisi turları (serbest bırakma sonrası)", async () =>
            {
                for (var i = 0; i < 5; i++)
                    await RunRemindersAsync(factory.Services);
                return 0;
            });
            var dispatchAfter = await DispatchesAsync(factory.Services, ctx.TenantId, idA);
            meter.Note($"içe aktarılan için günlük {dispatchAfter.Imported} (gönderim {dispatchAfter.ImportedSent}, atlandı/e-postasız {dispatchAfter.ImportedSkipped})");
            Check("Serbest bırakma sonrası pencere içi içe aktarılmış aşılar değerlendirildi", dispatchAfter.Imported == importedInWindow && dispatchAfter.ImportedSent == emailsInWindow,
                $"günlük {dispatchAfter.Imported} / pencere {importedInWindow}; gönderim {dispatchAfter.ImportedSent} / e-postalı {emailsInWindow}");
            var dispatchB = await DispatchesAsync(factory.Services, ctx.TenantId, idB);
            Check("Serbest bırakılmamış B partisinin aşılarına hatırlatma üretilmedi", dispatchB.Imported == 0, $"{dispatchB}");

            // ------------------------------------------------------------ Geri alma (bağı olan/olmayan karışımı)
            var blockers = await AddRollbackBlockersAsync(factory.Services, ctx, idA);
            report.Line();
            report.Line($"Geri alma engelleri eklendi: {blockers.Visits} hastaya geliş, {blockers.EditedClients} müşteri düzenlendi.");

            var rbPreview1 = await meter.RunAsync("A geri alma önizleme (B commit'liyken)", async () => await GetJsonAsync(http, $"/api/v1/imports/batches/{idA}/rollback-preview"));
            meter.Note(RollbackPreviewNote(rbPreview1));

            sampler.Phase = "B geri alma";
            var rollbackB = await meter.RunAsync("B geri al (Fail) + arka plan", async () =>
            {
                using var r = await http.PostAsJsonAsync($"/api/v1/imports/batches/{idB}/rollback", new { requestId = Guid.NewGuid(), reason = "Perf denemesi B", blockedPolicy = "Fail" });
                var status = (int)r.StatusCode;
                return (status, wait: await WaitWhileAsync(http, idB, "RollingBack", TimeSpan.FromMinutes(60)));
            });
            meter.Note($"POST {rollbackB.status} → {rollbackB.wait}; geri basınç: {rollbackLog.Summary()}");
            Check("B geri alma: RolledBack", rollbackB.wait.Final == "RolledBack", $"{rollbackB}");

            sampler.Phase = "A geri alma";
            var rbPreview2 = await meter.RunAsync("A geri alma önizleme (B geri alındıktan sonra)", async () => await GetJsonAsync(http, $"/api/v1/imports/batches/{idA}/rollback-preview"));
            meter.Note(RollbackPreviewNote(rbPreview2));
            var refused = await meter.RunAsync("A geri al (Fail) → engel", async () =>
            {
                using var r = await http.PostAsJsonAsync($"/api/v1/imports/batches/{idA}/rollback", new { requestId = Guid.NewGuid(), reason = "Perf denemesi A", blockedPolicy = "Fail" });
                return ((int)r.StatusCode, await CodeAsync(r));
            });
            meter.Note($"HTTP {refused.Item1} {refused.Item2}");
            Check("A Fail geri alma: 409 Imports.RollbackBlocked", refused is (409, "Imports.RollbackBlocked"), $"{refused}");

            var rollbackA = await meter.RunAsync("A geri al (DeleteUnblockedOnly) + arka plan", async () =>
            {
                using var r = await http.PostAsJsonAsync($"/api/v1/imports/batches/{idA}/rollback", new { requestId = Guid.NewGuid(), reason = "Perf denemesi A kısmi", blockedPolicy = "DeleteUnblockedOnly" });
                var status = (int)r.StatusCode;
                return (status, wait: await WaitWhileAsync(http, idA, "RollingBack", TimeSpan.FromMinutes(90)));
            });
            meter.Note($"POST {rollbackA.status} → {rollbackA.wait}; geri basınç: {rollbackLog.Summary()}");
            Check("A kısmi geri alma: PartiallyRolledBack", rollbackA.wait.Final == "PartiallyRolledBack", $"{rollbackA}");
            sampler.Phase = "Geri alma sonrası";

            var rbFinal = await GetJsonAsync(http, $"/api/v1/imports/batches/{idA}/rollback-preview");
            var dbAfter = await TenantCountsAsync(factory.Services, ctx.TenantId);
            report.Line();
            report.Line($"A geri alma sonrası: {RollbackPreviewNote(rbFinal)}; kiracı sayıları {dbAfter}");
            var blockedAfter = rbFinal.GetProperty("blocked");
            Check("Geri alma sonrası kalan kayıt = mevcut veri + engelliler (eşleşen mevcut kayıt silinmedi)",
                dbAfter.Clients == ctx.ExistingClients + blockedAfter.GetProperty("clients").GetInt32()
                && dbAfter.Pets == ctx.ExistingPets + blockedAfter.GetProperty("pets").GetInt32()
                && dbAfter.Vaccinations == 1 + blockedAfter.GetProperty("vaccinations").GetInt32(),
                $"kiracı {dbAfter}, mevcut {ctx.ExistingClients}/{ctx.ExistingPets}/1, engelli {blockedAfter}");

            // ------------------------------------------------------------ Temizlik işi (30 gün sonrası)
            var purge = await meter.RunAsync("Bakım işi: 30 gün sonrası satır verisi temizliği", async () =>
            {
                var processor = new ImportMaintenanceProcessor(
                    scopes, Options.Create(new ImportMaintenanceOptions { BatchSize = 100 }),
                    new FakeTimeProvider(DateTime.UtcNow.AddDays(30).AddMinutes(5)), NullLogger<ImportMaintenanceProcessor>.Instance);
                return await processor.RunOnceAsync(CancellationToken.None);
            });
            var purgedRows = await PurgedRowCountAsync(factory.Services, idA, idB);
            meter.Note($"süresi dolan {purge.ExpiredBatches}, temizlenen parti {purge.PurgedBatches}; kişisel verisi kalan satır {purgedRows.Remaining} / {purgedRows.Total}");
            Check("30 gün temizliği: A ve B satır verisi boşaltıldı", purgedRows.Remaining == 0 && purge.PurgedBatches >= 2, $"{purge}, {purgedRows}");
        }
        finally
        {
            sampler.Phase = "Bitti";
            await validationWorker.StopAsync(CancellationToken.None);
            await commitWorker.StopAsync(CancellationToken.None);
            await rollbackWorker.StopAsync(CancellationToken.None);
            await sampler.StopAsync();

            report.Line();
            report.Line("## Aşamalar");
            report.Line(meter.ToMarkdown());
            report.Line("## Sağlık ve outbox örnekleri (1 sn; aşama başına)");
            report.Line(sampler.ToMarkdown());
            report.Line("## Kilit beklemeleri (örnekleyici, sys.dm_exec_requests; benzersiz bekleyen/bekleten çifti)");
            report.Line(sampler.BlockingMarkdown());
            report.Line("## İşçi SQL: en sık 12 komut metni (tüm koşu)");
            report.Line(capture.TopTexts(SqlBucketCapture.Worker, 12));
            report.Line("## İşçi SQL: toplam süresi en yüksek 8 komut");
            report.Line(capture.TopTexts(SqlBucketCapture.Worker, 8, bySum: true));
            report.Line("## HTTP SQL: en sık 8 komut (tüm koşu)");
            report.Line(capture.TopTexts(SqlBucketCapture.Http, 8));
            report.Line("## HTTP SQL: toplam süresi en yüksek 10 komut");
            report.Line(capture.TopTexts(SqlBucketCapture.Http, 10, bySum: true));
            report.Line("## HTTP SQL: okuyucu ömrü (okuma + somutlaştırma dahil) en yüksek 8 sorgu");
            report.Line(capture.TopReads(SqlBucketCapture.Http, 8));
            report.Line("## İşçi SQL: okuyucu ömrü en yüksek 6 sorgu");
            report.Line(capture.TopReads(SqlBucketCapture.Worker, 6));
            report.Line("## Sağlık ucu SQL: toplam süresi en yüksek 5 komut");
            report.Line(capture.TopTexts(SqlBucketCapture.Health, 5, bySum: true));
            report.Line("## Progress ucu SQL: toplam süresi en yüksek 5 komut");
            report.Line(capture.TopTexts(SqlBucketCapture.Progress, 5, bySum: true));
            report.Line("## Diğer arka plan SQL: toplam süresi en yüksek 6 komut");
            report.Line(capture.TopTexts(SqlBucketCapture.OtherBackground, 6, bySum: true));
            report.Line("## Denetimler");
            foreach (var (name, ok, detail) in checks)
                report.Line($"- [{(ok ? "x" : " ")}] {name} {(ok ? "" : "— " + detail)}");
            report.Line();
            report.Line($"Süreç tepe çalışma kümesi: {Process.GetCurrentProcess().PeakWorkingSet64 / 1024 / 1024} MB");
            report.Flush();
            _output.WriteLine($"Sonuç: {outPath}");
        }

        checks.Where(c => !c.Ok).Should().BeEmpty($"denetimler: {string.Join(" | ", checks.Where(c => !c.Ok).Select(c => $"{c.Name}: {c.Detail}"))}");
    }

    // ================================================================ Kiracı / veri

    private sealed record TenantCtx(Guid TenantId, Guid ClinicId, string Token, int ExistingClients, int ExistingPets);

    private static async Task<TenantCtx> SeedTenantAsync(IServiceProvider services, ImportPilotFileGenerator gen)
    {
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(services);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        IntegrationTestDatabaseGuard.EnsureSafeDatabase(db.Database.GetConnectionString());
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var tenant = new Tenant($"ImpPerf-{Guid.NewGuid():N}"[..18], DateTime.UtcNow);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        var clinic = new Clinic(tenant.Id, "ImpPerf-Ana", "Izmir");
        db.Clinics.Add(clinic);
        var settings = TenantReminderSettings.CreateDefault(tenant.Id, DateTime.UtcNow);
        settings.Update(false, 24, true, 3, true, DateTime.UtcNow); // aşı hatırlatması açık, 3 gün önce
        db.TenantReminderSettings.Add(settings);
        await db.SaveChangesAsync();

        // Sistemde zaten kayıtlı müşteriler (dosyayla aynı kişi, farklı yazım) ve çipi dosyayla çakışan hayvanlar.
        foreach (var (name, mobile) in gen.ExistingClients)
            db.Clients.Add(new Client(tenant.Id, name, DateTime.UtcNow, "90" + mobile[1..]));
        var cat = await db.Species.AsNoTracking().SingleAsync(s => s.Code == "CAT");
        var owner = new Client(tenant.Id, "Sistem Mevcut Sahip", DateTime.UtcNow, "905009990000");
        db.Clients.Add(owner);
        foreach (var chip in gen.ExistingChips)
            db.Pets.Add(new Pet(tenant.Id, owner.Id, $"Mevcut {chip[^4..]}", cat.Id, microchipNumber: chip));

        // Kontrol: elle girilmiş (içe aktarılmamış) aşı, vadesi hatırlatma penceresinde, sahibi e-postalı.
        var control = new Client(tenant.Id, "Kontrol Sahibi", DateTime.UtcNow, "905009990001", "kontrol@example.com");
        var controlPet = new Pet(tenant.Id, control.Id, "Kontrol", cat.Id);
        var rabies = await db.VaccineDefinitions.AsNoTracking().SingleAsync(v => v.TenantId == null && v.Code == "RABIES");
        var now = DateTime.UtcNow;
        var vaccination = new Vaccination(tenant.Id, controlPet.Id, clinic.Id, null, rabies.Id, rabies.Name, VaccinationStatus.Applied, now.AddDays(-360), now.AddDays(2), null, DateTime.UtcNow);
        db.Clients.Add(control);
        db.Pets.Add(controlPet);
        db.Vaccinations.Add(vaccination);
        await db.SaveChangesAsync();

        var token = await IntegrationTestAuthHelper.SeedScopedClinicUserAndIssueTokenAsync(db, jwt, hasher, tenant.Id, clinic.Id, Permissions);
        return new TenantCtx(tenant.Id, clinic.Id, token, gen.ExistingClients.Count + 2, gen.ExistingChips.Count + 1);
    }

    private static async Task<ImportCommitPlan> PlanAsync(IServiceProvider services, Guid id)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.ImportBatchRows.AsNoTracking().Where(r => r.BatchId == id)
            .Select(r => new ImportCommitRowInfo(r.Id, r.Table, r.RowNumber, r.ValidationStatus, r.ApplyStatus, r.NormalizedJson))
            .ToListAsync();
        return ImportCommitPlanner.Build(rows);
    }

    private static async Task<(int Clients, int Pets, int Vaccinations)> TenantCountsAsync(IServiceProvider services, Guid tenantId)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Clients.CountAsync(c => c.TenantId == tenantId),
            await db.Pets.CountAsync(p => p.TenantId == tenantId),
            await db.Vaccinations.CountAsync(v => v.TenantId == tenantId));
    }

    private static async Task<int> ImportedDueInWindowAsync(IServiceProvider services, Guid batchId)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        return await db.Vaccinations.CountAsync(v => v.ImportBatchId == batchId && v.Status == VaccinationStatus.Applied
                                                     && v.DueAtUtc > now && v.DueAtUtc <= now.AddDays(3));
    }

    private static async Task<int> RunRemindersAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReminderProcessorService>().ProcessOnceAsync(CancellationToken.None);
        return 0;
    }

    private sealed record Dispatches(int Imported, int ImportedSent, int ImportedSkipped, int Control);

    private static async Task<Dispatches> DispatchesAsync(IServiceProvider services, Guid tenantId, Guid batchId)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var imported = db.Vaccinations.Where(v => v.ImportBatchId == batchId).Select(v => v.Id);
        var logs = await db.ReminderDispatchLogs.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.ReminderType == ReminderType.Vaccination)
            .Select(l => new { l.SourceEntityId, l.Status, IsImported = imported.Contains(l.SourceEntityId) })
            .ToListAsync();
        return new Dispatches(
            logs.Count(l => l.IsImported),
            logs.Count(l => l.IsImported && l.Status is ReminderDispatchStatus.Enqueued or ReminderDispatchStatus.Sent),
            logs.Count(l => l.IsImported && l.Status == ReminderDispatchStatus.Skipped),
            logs.Count(l => !l.IsImported));
    }

    /// <summary>Gölge kullanım benzetimi: oluşturulan hastaların bir kısmına geliş açılır, bazı müşteriler düzenlenir.</summary>
    private static async Task<(int Visits, int EditedClients)> AddRollbackBlockersAsync(IServiceProvider services, TenantCtx ctx, Guid batchId)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var created = await db.ImportBatchRows.AsNoTracking()
            .Where(r => r.BatchId == batchId && r.ApplyStatus == ImportRowApplyStatus.Created && r.TargetEntityId != null)
            .Select(r => new { r.Table, Id = r.TargetEntityId!.Value })
            .ToListAsync();
        var pets = created.Where(r => r.Table == ImportTable.Pets).Select(r => r.Id).ToList();
        var clients = created.Where(r => r.Table == ImportTable.Customers).Select(r => r.Id).ToList();

        var visitPets = pets.Where((_, i) => i % 200 == 7).ToList();       // ~%0,5
        foreach (var petId in visitPets)
            db.Visits.Add(new Visit(ctx.TenantId, ctx.ClinicId, petId, null, null, Guid.NewGuid(), DateTime.UtcNow));

        var editIds = clients.Where((_, i) => i % 500 == 11).ToList();    // ~%0,2
        var edited = await db.Clients.Where(c => editIds.Contains(c.Id)).ToListAsync();
        foreach (var c in edited)
            c.UpdateDetails(c.FullName, $"duzenlendi-{c.Id:N}"[..20] + "@example.com", c.Phone, c.Address, DateTime.UtcNow);

        await db.SaveChangesAsync();
        return (visitPets.Count, edited.Count);
    }

    private static async Task<(int Total, int Remaining)> PurgedRowCountAsync(IServiceProvider services, params Guid[] batchIds)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = db.ImportBatchRows.Where(r => batchIds.Contains(r.BatchId));
        return (await rows.CountAsync(), await rows.CountAsync(r => r.SourceJson != null || r.NormalizedJson != null || r.IssuesJson != null));
    }

    // ================================================================ HTTP yardımcıları

    private static Task<HttpResponseMessage> PostXlsxAsync(HttpClient http, byte[] bytes, Guid clinicId)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(Guid.NewGuid().ToString()), "requestId" },
            { new StringContent(clinicId.ToString()), "clinicId" },
            { new StringContent("Perf A"), "name" },
        };
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(ImportPilotFileGenerator.XlsxContentType);
        form.Add(content, "file", "klinik-aktarim.xlsx");
        return http.PostAsync("/api/v1/imports/batches", form);
    }

    private static Task<HttpResponseMessage> PostCsvAsync(HttpClient http, Guid clinicId, byte[]? customers, byte[]? pets, byte[]? vaccinations)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(Guid.NewGuid().ToString()), "requestId" },
            { new StringContent(clinicId.ToString()), "clinicId" },
        };
        void Add(byte[]? bytes, string field, string file)
        {
            if (bytes is null)
                return;
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
            form.Add(content, field, file);
        }

        Add(customers, "customersFile", "musteriler.csv");
        Add(pets, "petsFile", "hayvanlar.csv");
        Add(vaccinations, "vaccinationsFile", "asilar.csv");
        return http.PostAsync("/api/v1/imports/batches", form);
    }

    private static async Task<JsonElement> ExpectJsonAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {body[..Math.Min(body.Length, 2000)]}");
            return JsonDocument.Parse(body).RootElement.Clone();
        }
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient http, string url) => await ExpectJsonAsync(await http.GetAsync(url));

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
            return null;
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>
    /// Doğrulama (K13, arka plan): POST hemen 202 döner (istek yanıt süresi ayrıca not edilir), sonuç <c>progress</c>
    /// 250 ms aralıkla yoklanarak beklenir (aşama süresi önceki senkron ölçümle karşılaştırılabilir kalsın); son parti döner.
    /// </summary>
    private static async Task<JsonElement> ValidateAsync(PhaseMeter meter, HttpClient http, Guid id, string phase)
    {
        long postMs = 0;
        var wait = await meter.RunAsync(phase, async () =>
        {
            var post = Stopwatch.StartNew();
            using (var r = await http.PostAsync($"/api/v1/imports/batches/{id}/validate", null))
            {
                postMs = post.ElapsedMilliseconds;
                if (r.StatusCode != HttpStatusCode.Accepted)
                    throw new InvalidOperationException($"Doğrulama 202 dönmedi: HTTP {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
            }

            return await WaitWhileAsync(http, id, "Validating", TimeSpan.FromMinutes(30), pollMs: 250);
        });
        meter.Note($"POST 202 {postMs} ms; {wait}; en çok işlenen satır {wait.MaxProcessed}");
        return await GetJsonAsync(http, $"/api/v1/imports/batches/{id}");
    }

    /// <summary>İstemci gibi <paramref name="pollMs"/> (varsayılan 2 sn) aralıkla <c>progress</c> yoklar; durum <paramref name="whileStatus"/> dışına çıkınca döner.</summary>
    /// <remarks>Hatalı yanıt (ör. 500) işi durdurmaz: arayüz gibi yoklamaya devam edilir, sayı ve ilk hata kaydedilir.</remarks>
    private static async Task<ProgressWait> WaitWhileAsync(HttpClient http, Guid id, string whileStatus, TimeSpan timeout, int pollMs = 2000)
    {
        var sw = Stopwatch.StartNew();
        var polls = 0;
        var maxProcessed = 0;
        var errors = 0;
        string? firstError = null;
        long maxPollMs = 0;
        while (true)
        {
            var poll = Stopwatch.StartNew();
            using var response = await http.GetAsync($"/api/v1/imports/batches/{id}/progress");
            var body = await response.Content.ReadAsStringAsync();
            maxPollMs = Math.Max(maxPollMs, poll.ElapsedMilliseconds);
            polls++;
            if (!response.IsSuccessStatusCode)
            {
                errors++;
                firstError ??= $"HTTP {(int)response.StatusCode} {body[..Math.Min(body.Length, 300)]}";
                if (sw.Elapsed > timeout)
                    return new ProgressWait("Timeout", polls, maxProcessed, errors, firstError, maxPollMs);
                await Task.Delay(pollMs);
                continue;
            }

            using var progress = JsonDocument.Parse(body);
            maxProcessed = Math.Max(maxProcessed, progress.RootElement.GetProperty("processed").GetInt32());
            var status = progress.RootElement.GetProperty("status").GetString()!;
            if (status != whileStatus || sw.Elapsed > timeout)
                return new ProgressWait(status, polls, maxProcessed, errors, firstError, maxPollMs);
            await Task.Delay(pollMs);
        }
    }

    private sealed record ProgressWait(string Final, int Polls, int MaxProcessed, int Errors, string? FirstError, long MaxPollMs)
    {
        public override string ToString()
            => $"{Final}; {Polls} yoklama, en yavaş {MaxPollMs} ms, hatalı yanıt {Errors}{(FirstError is null ? "" : $" (ilk: {FirstError})")}";
    }

    private static Dictionary<string, Dictionary<string, string>> SuggestedColumns(JsonElement batch)
        => batch.GetProperty("tables").EnumerateArray().ToDictionary(
            t => t.GetProperty("table").GetString()!,
            t => t.GetProperty("columns").EnumerateArray()
                .Where(c => c.GetProperty("suggested").ValueKind == JsonValueKind.String)
                .ToDictionary(c => c.GetProperty("suggested").GetString()!, c => c.GetProperty("source").GetString()!));

    private static List<string> UnsuggestedColumns(JsonElement batch)
        => batch.GetProperty("tables").EnumerateArray()
            .SelectMany(t => t.GetProperty("columns").EnumerateArray())
            .Where(c => c.GetProperty("suggested").ValueKind != JsonValueKind.String)
            .Select(c => c.GetProperty("source").GetString()!)
            .ToList();

    private static string SuggestionNote(JsonElement batch)
        => string.Join("; ", batch.GetProperty("tables").EnumerateArray().Select(t =>
            $"{t.GetProperty("table").GetString()} {t.GetProperty("rowCount").GetInt32()} satır, öneri " +
            string.Join("/", t.GetProperty("columns").EnumerateArray().GroupBy(c => c.GetProperty("confidence").GetString()).Select(g => $"{g.Key}:{g.Count()}"))));

    /// <summary>Bloklayan eşlenemeyen tür/aşı değerleri bilinçli atlanır (ADR-019 K6: katalog genişletilmez).</summary>
    private static object[] SkipMappings(JsonElement preview)
        => preview.GetProperty("unmappedValues").EnumerateArray()
            .Where(u => u.GetProperty("blocking").GetBoolean())
            .Select(u => (object)new { kind = u.GetProperty("kind").GetString(), source = u.GetProperty("source").GetString(), targetId = (Guid?)null })
            .ToArray();

    private static string UnmappedNote(JsonElement preview)
        => string.Join(", ", preview.GetProperty("unmappedValues").EnumerateArray().Select(u =>
            $"{u.GetProperty("kind").GetString()}:{u.GetProperty("rowCount").GetInt32()}{(u.GetProperty("blocking").GetBoolean() ? "!" : "")}"));

    private static string IssueNote(JsonElement preview)
        => string.Join(", ", preview.GetProperty("issueCounts").EnumerateArray().Select(i =>
            $"{i.GetProperty("code").GetString()}/{i.GetProperty("severity").GetString()![0]}={i.GetProperty("rowCount").GetInt32()}"));

    private static readonly string[] Tables = ["customers", "pets", "vaccinations"];

    private static string SummaryNote(JsonElement batch)
        => string.Join("; ", Tables.Select(t =>
        {
            var s = batch.GetProperty("summary").GetProperty(t);
            return $"{t} toplam {s.GetProperty("total").GetInt32()} geçerli {s.GetProperty("valid").GetInt32()} uyarı {s.GetProperty("warning").GetInt32()} " +
                   $"hata {s.GetProperty("error").GetInt32()} → oluşacak {s.GetProperty("willCreate").GetInt32()} eşleşecek {s.GetProperty("willMatch").GetInt32()} atlanacak {s.GetProperty("willSkip").GetInt32()}";
        }));

    private static string SummaryKey(JsonElement batch)
        => string.Join("|", Tables.Select(t =>
        {
            var s = batch.GetProperty("summary").GetProperty(t);
            return string.Join(",", new[] { "total", "valid", "warning", "error", "willCreate", "willMatch", "willSkip" }.Select(k => s.GetProperty(k).GetInt32()));
        }));

    private static string AppliedNote(JsonElement batch)
        => string.Join("; ", Tables.Select(t =>
        {
            var s = batch.GetProperty("summary").GetProperty(t);
            return $"{t} oluştu {s.GetProperty("created").GetInt32()} eşleşti {s.GetProperty("matched").GetInt32()} atlandı {s.GetProperty("skipped").GetInt32()} başarısız {s.GetProperty("failed").GetInt32()}";
        }));

    private static void CheckSummaryInvariants(JsonElement batch, string name, Action<string, bool, string> check, bool applied)
    {
        foreach (var t in Tables)
        {
            var s = batch.GetProperty("summary").GetProperty(t);
            int V(string k) => s.GetProperty(k).GetInt32();
            if (V("total") == 0)
                continue;
            check($"{name} {t}: valid+warning+error = total", V("valid") + V("warning") + V("error") == V("total"), SummaryNote(batch));
            check($"{name} {t}: willCreate+willMatch+willSkip = valid+warning", V("willCreate") + V("willMatch") + V("willSkip") == V("valid") + V("warning"), SummaryNote(batch));
            if (applied)
                check($"{name} {t}: created+matched+skipped+failed = total", V("created") + V("matched") + V("skipped") + V("failed") == V("total"), AppliedNote(batch));
        }
    }

    private static string RollbackPreviewNote(JsonElement p)
    {
        string C(string prop) => $"{p.GetProperty(prop).GetProperty("clients").GetInt32()}/{p.GetProperty(prop).GetProperty("pets").GetInt32()}/{p.GetProperty(prop).GetProperty("vaccinations").GetInt32()}";
        var reasons = p.GetProperty("blockedItems").EnumerateArray()
            .SelectMany(i => i.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()))
            .GroupBy(r => r).Select(g => $"{g.Key}:{g.Count()}");
        return $"silinebilir {C("deletable")}, engelli {C("blocked")}, geri alınmış {C("rolledBack")} (müşteri/hasta/aşı); kept {p.GetProperty("kept")}; " +
               $"neden (ilk 200 öğe) [{string.Join(",", reasons)}]; canRollback={p.GetProperty("canRollback").GetBoolean()}, " +
               $"canDeleteUnblockedOnly={p.GetProperty("canDeleteUnblockedOnly").GetBoolean()}; uyarılar [{string.Join(",", p.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()))}]";
    }

    private static string Ratio(int a, int b) => b == 0 ? "-" : (a / (double)b).ToString("F1", CultureInfo.InvariantCulture);

    /// <summary>İşçiyi AsyncLocal etiketiyle başlatır: arka plan döngüsünün tüm SQL komutları işçiye atfedilir.</summary>
    private static async Task StartTaggedAsync(IHostedService service, string tag)
    {
        SqlBucketCapture.Tag.Value = tag;
        await service.StartAsync(CancellationToken.None);
    }

    // ================================================================ Ölçüm altyapısı

    /// <summary>Rapor dosyası; her aşamadan sonra diske yazılır (koşu yarıda kesilse de ham çıktı kalır).</summary>
    private sealed class Report(string path)
    {
        private readonly StringBuilder _sb = new();

        public void Line(string text = "") => _sb.AppendLine(text);

        public void Flush() => File.WriteAllText(path, _sb.ToString());
    }

    /// <summary>
    /// EF komutlarını kaynağa göre sayar: HTTP isteği (yol: sağlık / progress / diğer), işçi (AsyncLocal etiketi),
    /// örnekleyici, diğer arka plan (projeksiyon, outbox, hatırlatma hosted service'leri).
    /// </summary>
    internal sealed class SqlBucketCapture : DbCommandInterceptor
    {
        public const string Http = "http";
        public const string Health = "http:health";
        public const string Progress = "http:progress";
        public const string Worker = "işçi";
        public const string Sampler = "örnekleyici";
        public const string OtherBackground = "diğer-arka-plan";

        public static readonly AsyncLocal<string?> Tag = new();
        private static readonly HttpContextAccessor Accessor = new();

        private readonly ConcurrentDictionary<string, int> _counts = new();
        private readonly ConcurrentDictionary<string, double> _ms = new();
        private readonly ConcurrentDictionary<(string Bucket, string Text), (int Count, double Ms, double MaxMs)> _texts = new();

        // Okuyucu ömrü (yürütme + satırların okunması + EF somutlaştırma): yürütme süresi akışla okunan büyük sonuçları göstermez.
        private readonly ConcurrentDictionary<string, double> _readMs = new();
        private readonly ConcurrentDictionary<(string Bucket, string Text), (int Count, double Ms, double MaxMs, long Rows)> _reads = new();

        public Dictionary<string, (int Count, double Ms, double ReadMs)> Snapshot()
            => _counts.Keys.ToDictionary(k => k, k => (_counts.GetValueOrDefault(k), _ms.GetValueOrDefault(k), _readMs.GetValueOrDefault(k)));

        /// <param name="bySum">true: toplam süreye göre (en pahalı), false: sayıya göre (en sık).</param>
        public string TopTexts(string bucket, int top, bool bySum = false)
        {
            var sb = new StringBuilder("```\n");
            var rows = _texts.Where(kv => kv.Key.Bucket == bucket);
            rows = bySum ? rows.OrderByDescending(kv => kv.Value.Ms) : rows.OrderByDescending(kv => kv.Value.Count);
            foreach (var kv in rows.Take(top))
                sb.AppendLine($"x{kv.Value.Count} toplam {kv.Value.Ms:F0} ms, en uzun {kv.Value.MaxMs:F0} ms: {Flatten(kv.Key.Text, 220)}");
            return sb.Append("```\n").ToString();
        }

        /// <summary>Okuyucu ömrü (okuma dahil) toplamına göre en pahalı sorgular.</summary>
        public string TopReads(string bucket, int top)
        {
            var sb = new StringBuilder("```\n");
            foreach (var kv in _reads.Where(kv => kv.Key.Bucket == bucket).OrderByDescending(kv => kv.Value.Ms).Take(top))
                sb.AppendLine($"x{kv.Value.Count} okuma dahil toplam {kv.Value.Ms:F0} ms, en uzun {kv.Value.MaxMs:F0} ms, {kv.Value.Rows} satır: {Flatten(kv.Key.Text, 220)}");
            return sb.Append("```\n").ToString();
        }

        public override InterceptionResult DataReaderDisposing(DbCommand command, DataReaderDisposingEventData eventData, InterceptionResult result)
        {
            var bucket = Bucket();
            var ms = eventData.Duration.TotalMilliseconds;
            _readMs.AddOrUpdate(bucket, ms, (_, v) => v + ms);
            _reads.AddOrUpdate((bucket, command.CommandText), (1, ms, ms, eventData.ReadCount),
                (_, v) => (v.Count + 1, v.Ms + ms, Math.Max(v.MaxMs, ms), v.Rows + eventData.ReadCount));
            return result;
        }

        private static string Flatten(string text, int max)
        {
            var flat = string.Join(' ', text.Split((char[])['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
            return flat.Length <= max ? flat : flat[..max] + "…";
        }

        private static string Bucket()
        {
            var ctx = Accessor.HttpContext;
            if (ctx is not null)
            {
                var path = ctx.Request.Path.Value ?? "";
                return path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ? Health
                    : path.EndsWith("/progress", StringComparison.OrdinalIgnoreCase) ? Progress
                    : Http;
            }

            return Tag.Value ?? OtherBackground;
        }

        private void Add(DbCommand command, CommandExecutedEventData data)
        {
            var bucket = Bucket();
            _counts.AddOrUpdate(bucket, 1, (_, v) => v + 1);
            _ms.AddOrUpdate(bucket, data.Duration.TotalMilliseconds, (_, v) => v + data.Duration.TotalMilliseconds);
            var ms = data.Duration.TotalMilliseconds;
            _texts.AddOrUpdate((bucket, command.CommandText), (1, ms, ms), (_, v) => (v.Count + 1, v.Ms + ms, Math.Max(v.MaxMs, ms)));
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            Add(command, eventData);
            return ValueTask.FromResult(result);
        }

        public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
        {
            Add(command, eventData);
            return result;
        }

        public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
        {
            Add(command, eventData);
            return ValueTask.FromResult(result);
        }

        public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
        {
            Add(command, eventData);
            return result;
        }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Add(command, eventData);
            return ValueTask.FromResult(result);
        }

        public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
        {
            Add(command, eventData);
            return result;
        }
    }

    private sealed class PhaseRecord
    {
        public required string Name { get; init; }
        public required double Ms { get; init; }
        public required Dictionary<string, int> Commands { get; init; }
        public required double HttpSqlMs { get; init; }
        public required double HttpReadMs { get; init; }
        public required long AllocatedMb { get; init; }
        public required int Gen0 { get; init; }
        public required int Gen1 { get; init; }
        public required int Gen2 { get; init; }
        public required long WorkingSetMb { get; init; }
        public required long HeapMb { get; init; }
        public string Note { get; set; } = "";
    }

    private sealed class PhaseMeter(SqlBucketCapture capture)
    {
        private readonly List<PhaseRecord> _records = [];

        public PhaseRecord Last => _records[^1];

        public void Note(string note) => _records[^1].Note = _records[^1].Note.Length == 0 ? note : $"{_records[^1].Note}; {note}";

        public async Task<T> RunAsync<T>(string name, Func<Task<T>> action)
        {
            var before = capture.Snapshot();
            var alloc = GC.GetTotalAllocatedBytes();
            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
            var sw = Stopwatch.StartNew();
            var result = await action();
            sw.Stop();
            var after = capture.Snapshot();
            var commands = after.ToDictionary(kv => kv.Key, kv => kv.Value.Count - before.GetValueOrDefault(kv.Key).Count)
                .Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => kv.Value);
            using var process = Process.GetCurrentProcess();
            _records.Add(new PhaseRecord
            {
                Name = name,
                Ms = sw.Elapsed.TotalMilliseconds,
                Commands = commands,
                HttpSqlMs = after.GetValueOrDefault(SqlBucketCapture.Http).Ms - before.GetValueOrDefault(SqlBucketCapture.Http).Ms,
                HttpReadMs = after.GetValueOrDefault(SqlBucketCapture.Http).ReadMs - before.GetValueOrDefault(SqlBucketCapture.Http).ReadMs,
                AllocatedMb = (GC.GetTotalAllocatedBytes() - alloc) / 1024 / 1024,
                Gen0 = GC.CollectionCount(0) - g0,
                Gen1 = GC.CollectionCount(1) - g1,
                Gen2 = GC.CollectionCount(2) - g2,
                WorkingSetMb = process.WorkingSet64 / 1024 / 1024,
                HeapMb = GC.GetTotalMemory(false) / 1024 / 1024,
            });
            return result;
        }

        public string ToMarkdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine("| Aşama | Süre ms | SQL: HTTP | SQL: işçi | SQL: progress | SQL: sağlık | SQL: diğer arka plan | HTTP SQL ms (yürütme / okuyucu ömrü) | Ayrılan MB | GC 0/1/2 | Çalışma kümesi MB | Yığın MB | Not |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var r in _records)
            {
                int C(string b) => r.Commands.GetValueOrDefault(b);
                sb.AppendLine($"| {r.Name} | {r.Ms:F0} | {C(SqlBucketCapture.Http)} | {C(SqlBucketCapture.Worker)} | {C(SqlBucketCapture.Progress)} | {C(SqlBucketCapture.Health)} | " +
                              $"{C(SqlBucketCapture.OtherBackground)} | {r.HttpSqlMs:F0} / {r.HttpReadMs:F0} | {r.AllocatedMb} | {r.Gen0}/{r.Gen1}/{r.Gen2} | {r.WorkingSetMb} | {r.HeapMb} | {r.Note.Replace("|", "/")} |");
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// R4: her saniye <c>/health/ready</c> (HTTP) ve geri basınç ölçümü (<see cref="IImportBackpressureProbe"/>, sağlık
    /// kontrolleriyle aynı süzgeç) + kapalı hasta projeksiyonunun bekleyen olay sayısı. Aşama başına özetlenir.
    /// </summary>
    private sealed class HealthSampler(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<global::Program> factory) : IDisposable
    {
        private readonly ConcurrentQueue<Sample> _samples = new();
        private readonly CancellationTokenSource _cts = new();
        private Task? _loop;

        public volatile string Phase = "";

        private sealed record Sample(string Phase, DateTime At, int Status, string Degraded, int OutboxPending, int ClientPending, double ClientAge, int PetPending, long HealthMs);

        private readonly ConcurrentDictionary<string, (int Count, int MaxWaitMs, string Phase)> _blocking = new();
        private Task? _blockingLoop;

        public void Start()
        {
            _loop = Task.Run(LoopAsync);
            _blockingLoop = Task.Run(BlockingLoopAsync);
        }

        private const string BlockingSql = """
            SELECT r.wait_type, r.wait_time,
                   LEFT(REPLACE(REPLACE(t.text, CHAR(13), ' '), CHAR(10), ' '), 140) AS blocked_sql,
                   (SELECT LEFT(REPLACE(REPLACE(bt.text, CHAR(13), ' '), CHAR(10), ' '), 140)
                      FROM sys.dm_exec_connections c CROSS APPLY sys.dm_exec_sql_text(c.most_recent_sql_handle) bt
                     WHERE c.session_id = r.blocking_session_id) AS blocker_sql,
                   (SELECT s.open_transaction_count FROM sys.dm_exec_sessions s WHERE s.session_id = r.blocking_session_id) AS blocker_tx
            FROM sys.dm_exec_requests r CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t
            WHERE r.blocking_session_id <> 0 AND r.database_id = DB_ID()
            """;

        /// <summary>Kilit beklemesi anlık görüntüsü (ham ADO.NET; EF sayımına girmez), 500 ms'de bir.</summary>
        private async Task BlockingLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await using var conn = new Microsoft.Data.SqlClient.SqlConnection(IntegrationTestDatabaseGuard.DedicatedConnectionString);
                    await conn.OpenAsync(_cts.Token);
                    await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(BlockingSql, conn) { CommandTimeout = 5 };
                    await using var reader = await cmd.ExecuteReaderAsync(_cts.Token);
                    while (await reader.ReadAsync(_cts.Token))
                    {
                        var tx = reader.IsDBNull(4) ? "?" : reader.GetInt32(4).ToString(CultureInfo.InvariantCulture);
                        var key = $"{(reader.IsDBNull(0) ? "?" : reader.GetString(0))} | bekleyen: {reader.GetString(2)} | bekleten (açık işlem {tx}): {(reader.IsDBNull(3) ? "?" : reader.GetString(3))}";
                        var wait = reader.GetInt32(1);
                        var phase = Phase;
                        _blocking.AddOrUpdate(key, (1, wait, phase), (_, v) => (v.Count + 1, Math.Max(v.MaxWaitMs, wait), v.Phase));
                    }
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _blocking.AddOrUpdate($"örnekleme hatası {ex.GetType().Name}", (1, 0, Phase), (_, v) => (v.Count + 1, 0, v.Phase));
                }

                try
                {
                    await Task.Delay(500, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        public string BlockingMarkdown()
        {
            if (_blocking.IsEmpty)
                return "Kilit beklemesi gözlenmedi.\n";
            var sb = new StringBuilder("```\n");
            foreach (var kv in _blocking.OrderByDescending(kv => kv.Value.MaxWaitMs).Take(15))
                sb.AppendLine($"[{kv.Value.Phase}] x{kv.Value.Count}, en uzun {kv.Value.MaxWaitMs} ms: {kv.Key}");
            return sb.Append("```\n").ToString();
        }

        private async Task LoopAsync()
        {
            SqlBucketCapture.Tag.Value = SqlBucketCapture.Sampler;
            using var http = factory.CreateClient();
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var healthWatch = Stopwatch.StartNew();
                    using var response = await http.GetAsync("/health/ready");
                    var body = await response.Content.ReadAsStringAsync();
                    var healthMs = healthWatch.ElapsedMilliseconds;
                    var degraded = "";
                    if (response.StatusCode != HttpStatusCode.OK && body.StartsWith('{'))
                    {
                        using var doc = JsonDocument.Parse(body);
                        degraded = string.Join(",", doc.RootElement.GetProperty("results").EnumerateObject()
                            .Where(e => e.Value.GetProperty("status").GetString() != "Healthy")
                            .Select(e => $"{e.Name}:{e.Value.GetProperty("status").GetString()}"));
                    }

                    await using var scope = factory.Services.CreateAsyncScope();
                    var pressure = await scope.ServiceProvider.GetRequiredService<IImportBackpressureProbe>().ReadAsync(_cts.Token);
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var petPending = await OutboxMessageQueryFilters.PetIntegrationEventsOnly(db.OutboxMessages.AsNoTracking())
                        .CountAsync(m => m.ProcessedAtUtc == null && m.DeadLetterAtUtc == null, _cts.Token);
                    var client = pressure.Projections.Single(p => p.Name == "client-projection");
                    _samples.Enqueue(new Sample(Phase, DateTime.UtcNow, (int)response.StatusCode, degraded, pressure.OutboxPending,
                        client.Pending, client.OldestPendingAge?.TotalSeconds ?? 0, petPending, healthMs));
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _samples.Enqueue(new Sample(Phase, DateTime.UtcNow, -1, ex.GetType().Name, 0, 0, 0, 0, 0));
                }

                try
                {
                    await Task.Delay(1000, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        public async Task StopAsync()
        {
            await _cts.CancelAsync();
            if (_loop is not null)
                await _loop;
            if (_blockingLoop is not null)
                await _blockingLoop;
        }

        public string ToMarkdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine("| Aşama | Örnek | 503 örnek | İlk 503 (sn) | Degraded/Unhealthy kontroller | Genel outbox bekleyen (maks) | Müşteri proj. bekleyen (maks) | Müşteri proj. en eski yaş sn (maks) | Hasta olayı bekleyen (maks) | /health/ready ms (ortanca/maks) |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
            foreach (var group in _samples.GroupBy(s => s.Phase))
            {
                var list = group.ToList();
                var start = list[0].At;
                var first503 = list.FirstOrDefault(s => s.Status == 503);
                var degraded = string.Join(" ", list.Where(s => s.Degraded.Length > 0).SelectMany(s => s.Degraded.Split(',')).GroupBy(x => x).Select(g => $"{g.Key}×{g.Count()}"));
                sb.AppendLine($"| {group.Key} | {list.Count} | {list.Count(s => s.Status == 503)} | {(first503 is null ? "-" : (first503.At - start).TotalSeconds.ToString("F0", CultureInfo.InvariantCulture))} | " +
                              $"{(degraded.Length == 0 ? "-" : degraded)} | {list.Max(s => s.OutboxPending)} | {list.Max(s => s.ClientPending)} | {list.Max(s => s.ClientAge):F0} | {list.Max(s => s.PetPending)} | " +
                              $"{list.Select(s => s.HealthMs).Order().ElementAt(list.Count / 2)}/{list.Max(s => s.HealthMs)} |");
            }

            return sb.ToString();
        }

        public void Dispose() => _cts.Dispose();
    }

    /// <summary>İşlemci günlüklerini (yalnız mesaj şablonu ve parametreler; satır verisi yok) toplar: geri basınç özeti.</summary>
    private sealed class ListLogger<T> : ILogger<T>
    {
        private readonly ConcurrentQueue<(DateTime At, LogLevel Level, string Message)> _entries = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => _entries.Enqueue((DateTime.UtcNow, logLevel, formatter(state, exception)));

        public string Summary()
        {
            var list = _entries.ToList();
            var throttled = list.Where(e => e.Message.Contains("throttled", StringComparison.Ordinal)).ToList();
            var resumed = list.Where(e => e.Message.Contains("resumed", StringComparison.Ordinal)).ToList();
            var maxWait = list.Count(e => e.Message.Contains("max wait", StringComparison.Ordinal));
            var waitedSeconds = 0.0;
            foreach (var t in throttled)
            {
                var end = resumed.FirstOrDefault(r => r.At >= t.At);
                waitedSeconds += ((end.At == default ? DateTime.UtcNow : end.At) - t.At).TotalSeconds;
            }

            var reasons = throttled.Select(t => t.Message.Contains("Reason=") ? t.Message[(t.Message.IndexOf("Reason=", StringComparison.Ordinal) + 7)..] : "?")
                .Select(r => r.Split(' ')[0]).GroupBy(r => r).Select(g => $"{g.Key}×{g.Count()}");
            var errors = list.Count(e => e.Level >= LogLevel.Error);
            return $"bekleme {throttled.Count} kez, toplam ~{waitedSeconds:F0} sn, maks-bekleme aşımı {maxWait}, neden [{string.Join(",", reasons)}], hata günlüğü {errors}";
        }
    }
}

internal static class ImportPerfAssertions
{
    public static void Should204(this HttpStatusCode status, Action<string, bool, string> check, string name)
        => check(name, status == HttpStatusCode.NoContent, status.ToString());
}
