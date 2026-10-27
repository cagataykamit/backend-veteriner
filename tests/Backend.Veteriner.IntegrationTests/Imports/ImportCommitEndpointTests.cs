using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Clients.IntegrationEvents;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Imports.Commands.ApplyOwnerUnit;
using Backend.Veteriner.Application.Imports.Commit;
using Backend.Veteriner.Application.Projections.Clients;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Reminders;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Infrastructure.Imports;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Reminders;
using ClosedXML.Excel;
using AwesomeAssertions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Imports;

/// <summary>
/// İçe aktarma B4 (gerçek SQL Server/LocalDB): commit önkoşulları ve idempotency, eşzamanlı commit, arka plan işi
/// (sahip birimi atomikliği, K2/K4/K10), çökme/devralma ve kira çiti, hatırlatma dışlaması ve serbest bırakma, geri
/// basınç ölçümü, yetki/IDOR. Sentetik veri. Arka plan işçisi test yapılandırmasında kapalıdır; işçi testte doğrudan
/// çalıştırılır (biri gerçek hosted service ile).
/// </summary>
[Collection("imports-api")]
public sealed class ImportCommitEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] AdminPermissions =
    [
        "Imports.Read", "Imports.Manage", "Imports.Commit", "Clients.Create", "Pets.Create", "Vaccinations.Create",
    ];

    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string Chip = "985112345678901";

    private readonly CustomWebApplicationFactory _factory;

    public ImportCommitEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    // ================================================================ Ana akış (K2)

    [Fact]
    public async Task Commit_Should_Apply_Valid_Rows_In_Background_And_Skip_Error_Rows_K2()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var (id, validationId) = await UploadValidatedScenarioAsync(client, ctx);

        var response = await client.PostAsJsonAsync($"/api/v1/imports/batches/{id}/commit", new { validationId });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().Should().Be("Committing");

        var before = await GetJsonAsync(client, $"/api/v1/imports/batches/{id}/progress");
        before.GetProperty("phase").GetString().Should().Be("Committing");
        before.GetProperty("processed").GetInt32().Should().Be(0);
        before.GetProperty("total").GetInt32().Should().Be(13);

        await RunWorkerAsync();

        var progress = await GetJsonAsync(client, $"/api/v1/imports/batches/{id}/progress");
        progress.GetProperty("status").GetString().Should().Be("Committed");
        progress.GetProperty("phase").ValueKind.Should().Be(JsonValueKind.Null);
        progress.GetProperty("processed").GetInt32().Should().Be(13);
        progress.GetProperty("leaseActive").GetBoolean().Should().BeFalse();

        var batch = await GetJsonAsync(client, $"/api/v1/imports/batches/{id}");
        Applied(batch, "customers").Should().Equal(2, 0, 2, 0); // created, matched, skipped, failed
        Applied(batch, "pets").Should().Equal(2, 0, 2, 0);
        Applied(batch, "vaccinations").Should().Equal(3, 0, 2, 0);
        batch.GetProperty("committedAtUtc").ValueKind.Should().Be(JsonValueKind.String);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clients = await db.Clients.AsNoTracking().Where(c => c.TenantId == ctx.TenantId).ToListAsync();
        clients.Should().HaveCount(2, "M1 + M2; M4 M1'in mükerreri, M3 hatalı");
        var pets = await db.Pets.AsNoTracking().Where(p => p.TenantId == ctx.TenantId).ToListAsync();
        pets.Select(p => p.Name).Should().BeEquivalentTo("Pamuk", "Kehribar");
        pets.Single(p => p.Name == "Pamuk").MicrochipNumber.Should().Be(Chip);

        var vaccinations = await db.Vaccinations.AsNoTracking().Where(v => v.TenantId == ctx.TenantId).ToListAsync();
        vaccinations.Should().HaveCount(3);
        vaccinations.Should().OnlyContain(v => v.ImportBatchId == id && v.Status == VaccinationStatus.Applied
                                               && v.ExaminationId == null && v.ClinicId == ctx.ClinicId);
        (await db.Administrations.CountAsync(a => a.TenantId == ctx.TenantId)).Should().Be(0, "geçmiş aşı stok düşmez");

        // Satır sonuçları: hedef kimlikler aynı işlemde yazıldı; mükerrer satırlar kanonik kayda işaret eder.
        var rows = await db.ImportBatchRows.AsNoTracking().Where(r => r.BatchId == id).ToListAsync();
        rows.Should().NotContain(r => r.ApplyStatus == ImportRowApplyStatus.Pending);
        var m1 = rows.Single(r => r.Table == ImportTable.Customers && r.RowNumber == 2);
        m1.ApplyStatus.Should().Be(ImportRowApplyStatus.Created);
        m1.TargetEntityType.Should().Be("Client");
        clients.Select(c => c.Id).Should().Contain(m1.TargetEntityId!.Value);
        var m4 = rows.Single(r => r.Table == ImportTable.Customers && r.RowNumber == 5);
        m4.ApplyStatus.Should().Be(ImportRowApplyStatus.Skipped);
        m4.TargetEntityId.Should().Be(m1.TargetEntityId);
        rows.Single(r => r.Table == ImportTable.Customers && r.RowNumber == 4).ApplyStatus
            .Should().Be(ImportRowApplyStatus.Skipped, "doğrulama hatalı satır atlanır (K2)");
        var h1 = rows.Single(r => r.Table == ImportTable.Pets && r.RowNumber == 2);
        rows.Single(r => r.Table == ImportTable.Pets && r.RowNumber == 5).TargetEntityId.Should().Be(h1.TargetEntityId);
        rows.Where(r => r.Table == ImportTable.Vaccinations && r.ApplyStatus == ImportRowApplyStatus.Created)
            .Select(r => r.TargetEntityId!.Value).Should().BeEquivalentTo(vaccinations.Select(v => v.Id));

        // Mevcut oluşturma yolu: client.created outbox olayları üretildi.
        var clientEvents = await db.OutboxMessages.AsNoTracking()
            .Where(m => m.Type == ClientIntegrationEventTypes.Created && m.CreatedAtUtc > DateTime.UtcNow.AddMinutes(-5))
            .Select(m => m.Payload)
            .ToListAsync();
        clientEvents.Count(p => clients.Any(c => p.Contains(c.Id.ToString(), StringComparison.OrdinalIgnoreCase))).Should().Be(2);

        // Rows ucu uygulama durumuyla süzer.
        var created = await GetJsonAsync(client, $"/api/v1/imports/batches/{id}/rows?status=Created&pageSize=200");
        created.GetProperty("totalItems").GetInt32().Should().Be(7);

        // Uygulama sonrası önizleme etkisi gerçek kayıtlardan.
        var impact = (await GetJsonAsync(client, $"/api/v1/imports/batches/{id}/preview")).GetProperty("reminderImpact");
        impact.GetProperty("vaccinationsWithDueDate").GetInt32().Should().Be(3);
        impact.GetProperty("vaccinationsWithPastDueDate").GetInt32().Should().Be(1);
        impact.GetProperty("clientsWithEmail").GetInt32().Should().Be(1);
        impact.GetProperty("emailsWithinReminderWindow").GetInt32().Should().Be(1);

        // Audit: commit isteği ve arka plan tamamlanması; payload satır verisi taşımaz.
        var audits = await db.AuditLogs.AsNoTracking()
            .Where(a => a.TargetId == $"ImportBatchId={id}" && (a.Action == "ImportBatch.Commit" || a.Action == "ImportBatch.CommitCompleted"))
            .ToListAsync();
        audits.Select(a => a.Action).Should().BeEquivalentTo("ImportBatch.Commit", "ImportBatch.CommitCompleted");
        audits.Should().OnlyContain(a => a.RequestPayload == null || !a.RequestPayload.Contains("Ayşe", StringComparison.Ordinal));
    }

    // ================================================================ Idempotency ve eşzamanlılık

    [Fact]
    public async Task Concurrent_And_Repeated_Commit_Should_Start_A_Single_Job()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var (id, validationId) = await UploadValidatedScenarioAsync(client, ctx);

        var responses = await Task.WhenAll(
            client.PostAsJsonAsync($"/api/v1/imports/batches/{id}/commit", new { validationId }),
            ClientFor(ctx.Token).PostAsJsonAsync($"/api/v1/imports/batches/{id}/commit", new { validationId }));

        responses.Select(r => r.StatusCode).Should().BeEquivalentTo(new[] { HttpStatusCode.Accepted, HttpStatusCode.OK });

        await RunWorkerAsync();
        var again = await client.PostAsJsonAsync($"/api/v1/imports/batches/{id}/commit", new { validationId });
        again.StatusCode.Should().Be(HttpStatusCode.OK, "tekrar aynı özeti döner, yeni iş başlamaz");
        (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().Should().Be("Committed");
        await RunWorkerAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Clients.CountAsync(c => c.TenantId == ctx.TenantId)).Should().Be(2, "aynı satır iki kez yazılmaz");
        (await db.Vaccinations.CountAsync(v => v.TenantId == ctx.TenantId)).Should().Be(3);
        (await db.AuditLogs.CountAsync(a => a.TargetId == $"ImportBatchId={id}" && a.Action == "ImportBatch.CommitCompleted")).Should().Be(1);
    }

    [Fact]
    public async Task Commit_Preconditions_Should_Reject_Stale_FailIfAny_Blocked_And_Other_Active_Job()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var (id, validationId) = await UploadValidatedScenarioAsync(client, ctx);

        var stale = await client.PostAsJsonAsync($"/api/v1/imports/batches/{id}/commit", new { validationId = Guid.NewGuid() });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(stale)).Should().Be("Imports.ValidationStale");

        var failIfAny = await client.PostAsJsonAsync($"/api/v1/imports/batches/{id}/commit", new { validationId, onErrors = "FailIfAny" });
        failIfAny.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(failIfAny)).Should().Be("Imports.HasErrorRows");

        var badOption = await client.PostAsJsonAsync($"/api/v1/imports/batches/{id}/commit", new { validationId, onErrors = "Bogus" });
        badOption.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await StatusAsync(id)).Should().Be(ImportBatchStatus.Validated, "reddedilen istekler hiçbir şey başlatmaz");

        // Engel: eşlenmemiş tür (önizlemede canCommit = false) -> 409 CommitBlocked.
        var blockedId = await UploadAsync(client, ctx, Workbook(
            ("Musteriler", [["MusteriNo", "AdSoyad", "Telefon"], ["B1", $"Blok {Guid.NewGuid():N}"[..20], "05339998877"]]),
            ("Hayvanlar", [["MusteriNo", "HayvanNo", "HayvanAdi", "Tur"], ["B1", "BH1", "Duman", "Ejderha"]])));
        var blockedValidation = await ValidateAsync(client, blockedId);
        var blocked = await client.PostAsJsonAsync($"/api/v1/imports/batches/{blockedId}/commit", new { validationId = blockedValidation });
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await blocked.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("Imports.CommitBlocked");
        problem.GetProperty("blockers").EnumerateArray().Select(b => b.GetString()).Should().Contain("Imports.SpeciesUnmapped");

        // Kiracıda aktif iş varken ikinci parti commit (ve doğrulama) edilemez.
        var secondId = await UploadAsync(client, ctx, CustomersWorkbook(1));
        var secondValidation = await ValidateAsync(client, secondId);
        (await client.PostAsJsonAsync($"/api/v1/imports/batches/{id}/commit", new { validationId })).StatusCode
            .Should().Be(HttpStatusCode.Accepted);
        var second = await client.PostAsJsonAsync($"/api/v1/imports/batches/{secondId}/commit", new { validationId = secondValidation });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(second)).Should().Be("Imports.AnotherJobRunning");

        await RunWorkerAsync();
        (await StatusAsync(id)).Should().Be(ImportBatchStatus.Committed);
    }

    // ================================================================ Yetki / IDOR

    [Fact]
    public async Task Commit_Should_Require_Extra_Create_Permissions_And_Hide_Other_Tenant_Batches()
    {
        var ctx = await SeedAsync();
        var (id, validationId) = await UploadValidatedScenarioAsync(ClientFor(ctx.Token), ctx);

        var withoutPets = ClientFor(await TokenAsync(ctx, ["Imports.Read", "Imports.Commit", "Clients.Create", "Vaccinations.Create"]));
        var denied = await withoutPets.PostAsJsonAsync($"/api/v1/imports/batches/{id}/commit", new { validationId });
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(denied)).Should().Be("Imports.AccessDenied");

        var noCommit = ClientFor(await TokenAsync(ctx, ["Imports.Read", "Imports.Manage", "Clients.Create", "Pets.Create", "Vaccinations.Create"]));
        (await noCommit.PostAsJsonAsync($"/api/v1/imports/batches/{id}/commit", new { validationId })).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        var other = await SeedAsync();
        var otherClient = ClientFor(other.Token);
        (await otherClient.PostAsJsonAsync($"/api/v1/imports/batches/{id}/commit", new { validationId })).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await otherClient.GetAsync($"/api/v1/imports/batches/{id}/progress")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await otherClient.PostAsJsonAsync($"/api/v1/imports/batches/{id}/release-reminders", new { confirmEmailCount = 0 })).StatusCode
            .Should().Be(HttpStatusCode.NotFound);

        (await StatusAsync(id)).Should().Be(ImportBatchStatus.Validated);
    }

    // ================================================================ K4 / K10 (uygulama anında yeniden çözüm, birim atomikliği)

    [Fact]
    public async Task Commit_Should_Match_Client_Created_After_Preview_K4()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var (id, validationId) = await UploadValidatedScenarioAsync(client, ctx);

        // Önizlemeden sonra elle aynı ad + telefonla müşteri eklendi.
        Guid manualId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var manual = new Client(ctx.TenantId, ctx.CustomerName, DateTime.UtcNow, "905321234567", email: null, address: null);
            db.Clients.Add(manual);
            await db.SaveChangesAsync();
            manualId = manual.Id;
        }

        await CommitAndRunAsync(client, id, validationId);

        await using var check = _factory.Services.CreateAsyncScope();
        var data = check.ServiceProvider.GetRequiredService<AppDbContext>();
        (await data.Clients.CountAsync(c => c.TenantId == ctx.TenantId)).Should().Be(2, "M1 yeni oluşmadı; elle eklenen + M2");
        var m1 = await data.ImportBatchRows.AsNoTracking().SingleAsync(r => r.BatchId == id && r.Table == ImportTable.Customers && r.RowNumber == 2);
        m1.ApplyStatus.Should().Be(ImportRowApplyStatus.Matched);
        m1.Matched.Should().BeTrue();
        m1.TargetEntityId.Should().Be(manualId);
        m1.IssuesJson.Should().Contain("Imports.MatchedAtCommit");
        (await data.Pets.AsNoTracking().SingleAsync(p => p.TenantId == ctx.TenantId && p.Name == "Pamuk")).ClientId
            .Should().Be(manualId, "hayvanlar mevcut müşteriye bağlanır, mevcut bilgi ezilmez");
        (await data.Clients.AsNoTracking().SingleAsync(c => c.Id == manualId)).Email.Should().BeNull();
    }

    [Fact]
    public async Task Owner_Unit_Should_Be_Atomic_When_Microchip_Taken_After_Preview_K10()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var (id, validationId) = await UploadValidatedScenarioAsync(client, ctx);

        // Önizlemeden sonra çip başka bir hayvana kaydedildi.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var owner = new Client(ctx.TenantId, $"Başka {Guid.NewGuid():N}"[..20], DateTime.UtcNow, "905449998877", email: null, address: null);
            db.Clients.Add(owner);
            db.Pets.Add(new Pet(ctx.TenantId, owner.Id, "Çakışan", ctx.SpeciesId, microchipNumber: Chip));
            await db.SaveChangesAsync();
        }

        await CommitAndRunAsync(client, id, validationId);

        await using var check = _factory.Services.CreateAsyncScope();
        var data = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await data.ImportBatchRows.AsNoTracking().Where(r => r.BatchId == id).ToListAsync();
        var h1 = rows.Single(r => r.Table == ImportTable.Pets && r.RowNumber == 2);
        h1.ApplyStatus.Should().Be(ImportRowApplyStatus.Failed);
        h1.IssuesJson.Should().Contain("Imports.MicrochipConflict");
        var m1 = rows.Single(r => r.Table == ImportTable.Customers && r.RowNumber == 2);
        m1.ApplyStatus.Should().Be(ImportRowApplyStatus.Failed);
        m1.IssuesJson.Should().Contain("Imports.OwnerUnitFailed");
        m1.TargetEntityId.Should().BeNull();

        // M1 birimi bütünüyle geri alındı (müşteri dahil); M2 birimi etkilenmedi.
        (await data.Clients.AsNoTracking().CountAsync(c => c.TenantId == ctx.TenantId && c.FullName == ctx.CustomerName)).Should().Be(0);
        (await data.Pets.AsNoTracking().CountAsync(p => p.TenantId == ctx.TenantId && p.Name == "Kehribar")).Should().Be(1);
        (await data.Vaccinations.AsNoTracking().CountAsync(v => v.ImportBatchId == id)).Should().Be(1, "yalnız H2 aşısı");

        var batch = await GetJsonAsync(client, $"/api/v1/imports/batches/{id}");
        batch.GetProperty("status").GetString().Should().Be("Committed");
        Applied(batch, "customers").Should().Equal(1, 0, 1, 2);
        Applied(batch, "pets").Should().Equal(new[] { 1, 0, 1, 2 }, "H1 + mükerreri H5 aynı birimde başarısız");

        // Hata raporu uygulama hatalı satırları da içerir.
        (await client.GetAsync($"/api/v1/imports/batches/{id}/errors?format=csv")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ================================================================ Çökme, devralma, kira çiti

    [Fact]
    public async Task Crashed_Worker_Should_Be_Taken_Over_After_Lease_Expiry_Without_Double_Writes()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var (id, validationId) = await UploadValidatedScenarioAsync(client, ctx);
        (await client.PostAsJsonAsync($"/api/v1/imports/batches/{id}/commit", new { validationId })).EnsureSuccessStatusCode();

        // İşçi A partiyi aldı, ilk sahip birimini yazdı ve çöktü (kira hâlâ geçerli).
        const string crashed = "w-crashed";
        await MutateBatchAsync(id, b => b.ClaimCommitLease(crashed, DateTime.UtcNow).IsSuccess.Should().BeTrue());
        var plan = await PlanAsync(id);
        plan.Units.Should().HaveCount(2);
        (await SendUnitAsync(ctx.TenantId, id, crashed, plan.Units[0])).IsSuccess.Should().BeTrue();

        // Kira dolmadan başka işçi almaz.
        var takeover = Processor("w-takeover");
        (await takeover.ProcessNextAsync(CancellationToken.None)).Should().BeFalse();

        // Kira doldu: devralan işçi kalan birimi yazar, yazılmış birimi atlar.
        await MutateBatchAsync(id, b => b.ClaimCommitLease(crashed, DateTime.UtcNow - ImportBatch.CommitLeaseDuration - TimeSpan.FromSeconds(1)));
        (await takeover.ProcessNextAsync(CancellationToken.None)).Should().BeTrue();

        (await StatusAsync(id)).Should().Be(ImportBatchStatus.Committed);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Clients.CountAsync(c => c.TenantId == ctx.TenantId)).Should().Be(2);
            (await db.Pets.CountAsync(p => p.TenantId == ctx.TenantId)).Should().Be(2);
            (await db.Vaccinations.CountAsync(v => v.ImportBatchId == id)).Should().Be(3);
        }

        // Kira çiti: eski işçinin geç kalan birim yazımı reddedilir (hiçbir şey yazılmaz).
        var late = await SendUnitAsync(ctx.TenantId, id, crashed, plan.Units[1]);
        late.Error.Code.Should().Be(ImportOwnerUnitErrors.LeaseLostCode);
    }

    [Fact]
    public async Task Unit_From_Worker_Without_Lease_Should_Write_Nothing()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var (id, validationId) = await UploadValidatedScenarioAsync(client, ctx);
        (await client.PostAsJsonAsync($"/api/v1/imports/batches/{id}/commit", new { validationId })).EnsureSuccessStatusCode();

        await MutateBatchAsync(id, b => b.ClaimCommitLease("w-new", DateTime.UtcNow).IsSuccess.Should().BeTrue());
        var plan = await PlanAsync(id);

        var stale = await SendUnitAsync(ctx.TenantId, id, "w-old", plan.Units[0]);

        stale.Error.Code.Should().Be(ImportOwnerUnitErrors.LeaseLostCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Clients.CountAsync(c => c.TenantId == ctx.TenantId)).Should().Be(0);
        (await db.ImportBatchRows.CountAsync(r => r.BatchId == id && r.ApplyStatus != ImportRowApplyStatus.Pending)).Should().Be(0);

        // Partiyi kira sahibi bitirir (test sonrası takılı parti kalmasın).
        await MutateBatchAsync(id, b => b.ClaimCommitLease("w-new", DateTime.UtcNow - ImportBatch.CommitLeaseDuration - TimeSpan.FromSeconds(1)));
        await RunWorkerAsync();
        (await StatusAsync(id)).Should().Be(ImportBatchStatus.Committed);
    }

    [Fact]
    public async Task Hosted_Worker_Should_Finish_Commit_Without_Client_Polling()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var id = await UploadAsync(client, ctx, CustomersWorkbook(3));
        var validationId = await ValidateAsync(client, id);

        var options = Options.Create(new ImportCommitOptions
        {
            WorkerEnabled = true,
            PollIntervalSeconds = 1,
            Backpressure = new ImportBackpressureOptions { Enabled = false },
        });
        var hosted = new ImportCommitHostedService(
            Processor("w-hosted", options.Value), options, TimeProvider.System, NullLogger<ImportCommitHostedService>.Instance);
        await hosted.StartAsync(CancellationToken.None);
        try
        {
            (await client.PostAsJsonAsync($"/api/v1/imports/batches/{id}/commit", new { validationId })).StatusCode
                .Should().Be(HttpStatusCode.Accepted);

            // İstemci yoklamadan iş sürer; burada yalnız sonucu bekliyoruz.
            var sw = Stopwatch.StartNew();
            while ((await StatusAsync(id)) == ImportBatchStatus.Committing && sw.Elapsed < TimeSpan.FromSeconds(60))
                await Task.Delay(250);
        }
        finally
        {
            await hosted.StopAsync(CancellationToken.None);
        }

        (await StatusAsync(id)).Should().Be(ImportBatchStatus.Committed);
        await using var scope = _factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Clients.CountAsync(c => c.TenantId == ctx.TenantId)).Should().Be(3);
    }

    // ================================================================ Hatırlatma (K3)

    [Fact]
    public async Task Imported_Vaccinations_Should_Not_Produce_Reminders_Until_Released_K3()
    {
        var ctx = await SeedAsync(remindersEnabled: true);
        var client = ClientFor(ctx.Token);
        var (id, validationId) = await UploadValidatedScenarioAsync(client, ctx);

        var early = await client.PostAsJsonAsync($"/api/v1/imports/batches/{id}/release-reminders", new { confirmEmailCount = 1 });
        early.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(early)).Should().Be("Imports.InvalidState");

        await CommitAndRunAsync(client, id, validationId);

        // Kiracıda aşı hatırlatması açık ve pencerede bir aşı var; yine de içe aktarılan aşı aday değildir.
        await RunReminderProcessorAsync();
        var importedIds = await ImportedVaccinationIdsAsync(id);
        (await DispatchSourcesAsync(ctx.TenantId)).Should().NotIntersectWith(importedIds);

        var wrong = await client.PostAsJsonAsync($"/api/v1/imports/batches/{id}/release-reminders", new { confirmEmailCount = 5 });
        wrong.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await wrong.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("Imports.ReminderImpactChanged");
        problem.GetProperty("emailsWithinReminderWindow").GetInt32().Should().Be(1);

        var missing = await client.PostAsJsonAsync($"/api/v1/imports/batches/{id}/release-reminders", new { });
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var released = await client.PostAsJsonAsync($"/api/v1/imports/batches/{id}/release-reminders", new { confirmEmailCount = 1 });
        released.StatusCode.Should().Be(HttpStatusCode.OK, await released.Content.ReadAsStringAsync());
        var body = await released.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("alreadyReleased").GetBoolean().Should().BeFalse();
        body.GetProperty("note").ValueKind.Should().Be(JsonValueKind.Null, "kiracı ayarı açık");
        body.GetProperty("batch").GetProperty("remindersReleasedAtUtc").ValueKind.Should().Be(JsonValueKind.String);

        await RunReminderProcessorAsync();
        (await DispatchSourcesAsync(ctx.TenantId)).Intersect(importedIds).Should().ContainSingle("yalnız pencerede kalan, sahibi e-postalı aşı");

        var replay = await client.PostAsJsonAsync($"/api/v1/imports/batches/{id}/release-reminders", new { confirmEmailCount = 99 });
        replay.StatusCode.Should().Be(HttpStatusCode.OK, "idempotent; sayı karşılaştırılmaz");
        (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("alreadyReleased").GetBoolean().Should().BeTrue();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.AuditLogs.CountAsync(a => a.TargetId == $"ImportBatchId={id}" && a.Action == "ImportBatch.ReleaseReminders"))
            .Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Release_Reminders_Should_Note_When_Tenant_Reminders_Disabled()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var (id, validationId) = await UploadValidatedScenarioAsync(client, ctx);
        await CommitAndRunAsync(client, id, validationId);

        var released = await client.PostAsJsonAsync($"/api/v1/imports/batches/{id}/release-reminders", new { confirmEmailCount = 1 });

        released.StatusCode.Should().Be(HttpStatusCode.OK);
        (await released.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("note").GetString().Should().Contain("kapalı");
    }

    // ================================================================ Geri basınç (R4)

    [Fact]
    public async Task Backpressure_Should_Throttle_On_Real_Client_Projection_Backlog_And_Still_Finish()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var id = await UploadAsync(client, ctx, CustomersWorkbook(3));
        var validationId = await ValidateAsync(client, id);
        (await client.PostAsJsonAsync($"/api/v1/imports/batches/{id}/commit", new { validationId })).EnsureSuccessStatusCode();

        // Test ortamında müşteri projeksiyonu açık ama tüketmiyor (döngü 1 saat): birikim gerçektir.
        var options = new ImportCommitOptions
        {
            OwnersPerChunk = 1,
            Backpressure = new ImportBackpressureOptions { ProjectionPendingLimit = 1, WaitSeconds = 1, MaxWaitSeconds = 1 },
        };
        var sw = Stopwatch.StartNew();
        (await Processor("w-bp", options).ProcessNextAsync(CancellationToken.None)).Should().BeTrue();
        sw.Stop();

        (await StatusAsync(id)).Should().Be(ImportBatchStatus.Committed, "en uzun bekleme sonunda iş yine biter");
        sw.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(2), "2. ve 3. parça öncesi ≥ 1 sn beklendi");

        await using var scope = _factory.Services.CreateAsyncScope();
        var pressure = await scope.ServiceProvider.GetRequiredService<IImportBackpressureProbe>().ReadAsync(CancellationToken.None);
        var clientQueue = pressure.Projections.Single(p => p.Name == "client-projection");
        clientQueue.Enabled.Should().BeTrue();
        clientQueue.Pending.Should().BeGreaterThanOrEqualTo(3, "bu partinin client.created olayları kuyrukta");
        pressure.Projections.Single(p => p.Name == "pet-projection").Enabled.Should().BeFalse("kapalı projeksiyon beklenmez");

        // Ölçüm: aynı birikim tüketilmezse sağlık ucu Degraded (ready = 503) olur; işçi bundan önce (yaşın %60'ı) bekler.
        var status = await scope.ServiceProvider.GetRequiredService<IClientProjectionStatusReader>().GetStatusAsync(CancellationToken.None);
        status.PendingCount.Should().BeGreaterThanOrEqualTo(3);
        var healthOptions = new ClientProjectionHealthOptions();
        var aged = status with { OldestPendingAge = TimeSpan.FromSeconds(healthOptions.DegradedAfterSeconds) };
        ClientProjectionHealthEvaluator.Evaluate(aged, healthOptions, new QueryReadModelsOptions()).Level
            .Should().NotBe(ClientProjectionHealthLevel.Healthy);
        ImportBackpressurePolicy.WaitReason(
                pressure with { Projections = [clientQueue with { Pending = 1, OldestPendingAge = TimeSpan.FromSeconds(healthOptions.DegradedAfterSeconds * 0.6) }] },
                new ImportBackpressureOptions())
            .Should().NotBeNull("işçi sağlık eşiğine varmadan yavaşlar");
    }

    // ================================================================ Yardımcılar

    private static int[] Applied(JsonElement batch, string table)
    {
        var c = batch.GetProperty("summary").GetProperty(table);
        return new[] { "created", "matched", "skipped", "failed" }.Select(k => c.GetProperty(k).GetInt32()).ToArray();
    }

    private ImportCommitProcessor Processor(string workerId, ImportCommitOptions? options = null)
        => new(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options ?? new ImportCommitOptions { Backpressure = new ImportBackpressureOptions { Enabled = false } }),
            TimeProvider.System,
            NullLogger<ImportCommitProcessor>.Instance,
            workerId);

    /// <summary>Kuyruktaki tüm partileri bitirir (test işçisi; geri basınç kapalı: test ortamında projeksiyon tüketmez).</summary>
    private async Task RunWorkerAsync()
    {
        var processor = Processor($"w-test-{Guid.NewGuid():N}"[..20]);
        for (var i = 0; i < 20 && await processor.ProcessNextAsync(CancellationToken.None); i++)
        {
        }
    }

    private async Task CommitAndRunAsync(HttpClient client, Guid id, Guid validationId)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/imports/batches/{id}/commit", new { validationId });
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        await RunWorkerAsync();
        (await StatusAsync(id)).Should().Be(ImportBatchStatus.Committed);
    }

    private async Task<ImportCommitPlan> PlanAsync(Guid id)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.ImportBatchRows.AsNoTracking().Where(r => r.BatchId == id)
            .Select(r => new ImportCommitRowInfo(r.Id, r.Table, r.RowNumber, r.ValidationStatus, r.ApplyStatus, r.NormalizedJson))
            .ToListAsync();
        return ImportCommitPlanner.Build(rows);
    }

    private async Task<Backend.Veteriner.Domain.Shared.Result<ImportOwnerUnitResult>> SendUnitAsync(
        Guid tenantId, Guid batchId, string worker, ImportOwnerUnitPlan unit)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        using var tenant = scope.ServiceProvider.GetRequiredService<ITenantScope>().UseTenant(tenantId);
        try
        {
            return await scope.ServiceProvider.GetRequiredService<ISender>()
                .Send(new ApplyImportOwnerUnitCommand(batchId, worker, unit.OwnerKey, unit.RowIds));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Backend.Veteriner.Domain.Shared.Result<ImportOwnerUnitResult>.Failure(ImportOwnerUnitErrors.LeaseLost());
        }
    }

    private async Task MutateBatchAsync(Guid id, Action<ImportBatch> mutate)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var batch = await db.ImportBatches.SingleAsync(b => b.Id == id);
        mutate(batch);
        await db.SaveChangesAsync();
    }

    private async Task RunReminderProcessorAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReminderProcessorService>().ProcessOnceAsync(CancellationToken.None);
    }

    private async Task<List<Guid>> ImportedVaccinationIdsAsync(Guid batchId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Vaccinations.AsNoTracking()
            .Where(v => v.ImportBatchId == batchId).Select(v => v.Id).ToListAsync();
    }

    private async Task<List<Guid>> DispatchSourcesAsync(Guid tenantId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().ReminderDispatchLogs.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.ReminderType == ReminderType.Vaccination)
            .Select(l => l.SourceEntityId).ToListAsync();
    }

    /// <summary>
    /// 4 müşteri / 4 hasta / 5 aşı (toplam 13 satır): M1 e-postalı, M2 sabit hat (telefonsuz), M3 adsız (hata), M4 = M1
    /// mükerrer; H1 (M1, çipli), H2 (M2), H4 (M3 -> hata), H5 = H1 mükerrer; aşılar H1 ×3 (biri aynı gün mükerrer), H2, H4.
    /// Beklenen: 2 sahip birimi (M1, M2) + 3 hatalı satır.
    /// </summary>
    private async Task<(Guid Id, Guid ValidationId)> UploadValidatedScenarioAsync(HttpClient client, SeedCtx ctx)
    {
        var today = OperationDayBounds.ToLocalDate(DateTime.UtcNow);
        var bytes = Workbook(
            ("Musteriler",
            [
                ["MusteriNo", "AdSoyad", "Telefon", "Eposta"],
                ["M1", ctx.CustomerName, "05321234567", "m1@example.com"],
                ["M2", $"Veli {ctx.Suffix}", "02121234567", null],
                ["M3", null, "05331234567", null],
                ["M4", ctx.CustomerName, "+90 532 123 45 67", null],
            ]),
            ("Hayvanlar",
            [
                ["MusteriNo", "HayvanNo", "HayvanAdi", "Tur", "Mikrocip"],
                ["M1", "H1", "Pamuk", ctx.SpeciesName, Chip],
                ["M2", "H2", "Kehribar", ctx.SpeciesName, null],
                ["M3", "H4", "Hatalı", ctx.SpeciesName, null],
                ["M4", "H5", "Pamuk", ctx.SpeciesName, null],
            ]),
            ("Asilar",
            [
                ["HayvanNo", "AsiAdi", "UygulamaTarihi", "SonrakiAsiTarihi"],
                ["H1", ctx.VaccineName, "10.04.2024", Date(today.AddDays(2))],
                ["H1", ctx.VaccineName, "10.04.2023", "10.04.2024"],
                ["H2", ctx.VaccineName, "10.04.2024", Date(today.AddDays(30))],
                ["H1", ctx.VaccineName, "10.04.2024", null],
                ["H4", ctx.VaccineName, "10.04.2024", null],
            ]));

        var id = await UploadAsync(client, ctx, bytes);
        return (id, await ValidateAsync(client, id));
    }

    private static byte[] CustomersWorkbook(int count)
    {
        var rows = new List<object?[]> { new object?[] { "AdSoyad", "Telefon" } };
        for (var i = 0; i < count; i++)
            rows.Add([$"Toplu {Guid.NewGuid():N}"[..20], $"0532{Random.Shared.Next(1000000, 9999999)}"]);
        return Workbook(("Musteriler", rows.ToArray()));
    }

    private static string Date(DateOnly d) => d.ToString("dd.MM.yyyy");

    private static async Task<Guid> UploadAsync(HttpClient client, SeedCtx ctx, byte[] bytes)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(Guid.NewGuid().ToString()), "requestId" },
            { new StringContent(ctx.ClinicId.ToString()), "clinicId" },
        };
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(XlsxType);
        form.Add(content, "file", "aktar.xlsx");

        var response = await client.PostAsync("/api/v1/imports/batches", form);
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        var batch = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = batch.GetProperty("id").GetGuid();

        // Önerilen eşlemeyi (başlıklar kanonik) kaydet.
        var columns = batch.GetProperty("tables").EnumerateArray().ToDictionary(
            t => t.GetProperty("table").GetString()!,
            t => t.GetProperty("columns").EnumerateArray()
                .Where(c => c.GetProperty("suggested").ValueKind == JsonValueKind.String)
                .ToDictionary(c => c.GetProperty("suggested").GetString()!, c => c.GetProperty("source").GetString()!));
        var mapped = await client.PutAsJsonAsync($"/api/v1/imports/batches/{id}/mapping", new { columns, valueMappings = Array.Empty<object>() });
        mapped.StatusCode.Should().Be(HttpStatusCode.OK, await mapped.Content.ReadAsStringAsync());
        return id;
    }

    /// <summary>Arka plan doğrulamasını başlatıp bitirir (ADR-019 K13); güncel <c>validationId</c>.</summary>
    private async Task<Guid> ValidateAsync(HttpClient client, Guid id)
        => (await ImportValidationTestRunner.ValidateAsync(_factory.Services, client, id)).GetProperty("validationId").GetGuid();

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

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private HttpClient ClientFor(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<ImportBatchStatus> StatusAsync(Guid batchId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ImportBatches.AsNoTracking().Where(b => b.Id == batchId).Select(b => b.Status).SingleAsync();
    }

    private async Task<string> TokenAsync(SeedCtx ctx, string[] permissions)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        return await IntegrationTestAuthHelper.SeedScopedClinicUserAndIssueTokenAsync(db, jwt, hasher, ctx.TenantId, ctx.ClinicId, permissions);
    }

    /// <summary>Kiracı + klinik + teste özgü tür ve aşı tanımı + tam yetkili kullanıcı.</summary>
    private async Task<SeedCtx> SeedAsync(bool remindersEnabled = false)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var tenant = new Tenant($"imc-{Guid.NewGuid():N}"[..18], DateTime.UtcNow);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        var clinic = new Clinic(tenant.Id, $"Imc-{Guid.NewGuid():N}"[..14], "Istanbul");
        db.Clinics.Add(clinic);
        var species = new Species($"IMC{suffix}", $"Imc Tür {suffix}");
        var vaccine = VaccineDefinition.CreateTenant(tenant.Id, $"IMCV{suffix}", $"Imc Aşı {suffix}", DateTime.UtcNow);
        db.Species.Add(species);
        db.VaccineDefinitions.Add(vaccine);
        if (remindersEnabled)
        {
            var settings = TenantReminderSettings.CreateDefault(tenant.Id, DateTime.UtcNow);
            settings.Update(false, 24, true, 3, true, DateTime.UtcNow);
            db.TenantReminderSettings.Add(settings);
        }

        await db.SaveChangesAsync();

        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);
        var token = await IntegrationTestAuthHelper.SeedScopedClinicUserAndIssueTokenAsync(
            db, jwt, hasher, tenant.Id, clinic.Id, AdminPermissions);

        return new SeedCtx(tenant.Id, clinic.Id, token, suffix, species.Id, species.Name, vaccine.Name, $"Ayşe {suffix}");
    }

    private sealed record SeedCtx(
        Guid TenantId, Guid ClinicId, string Token, string Suffix, Guid SpeciesId, string SpeciesName, string VaccineName, string CustomerName);
}
