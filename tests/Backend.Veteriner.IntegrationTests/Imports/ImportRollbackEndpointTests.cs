using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Clients.IntegrationEvents;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Imports.Commands.ApplyOwnerUnit;
using Backend.Veteriner.Application.Imports.Commands.RollbackOwnerUnit;
using Backend.Veteriner.Application.Imports.Commit;
using Backend.Veteriner.Application.Imports.Rollback;
using Backend.Veteriner.Application.Pets.IntegrationEvents;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Domain.Visits;
using Backend.Veteriner.Infrastructure.Imports;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Web;
using ClosedXML.Excel;
using AwesomeAssertions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Imports;

/// <summary>
/// İçe aktarma B5 (gerçek SQL Server/LocalDB): rollback-preview doğruluğu, bağımlılık engeli (K7 <c>Fail</c>),
/// <c>DeleteUnblockedOnly</c>, yeniden deneme, idempotency ve eşzamanlı istek, iş sırasında doğan engel, çökme/devralma ve
/// kira çiti, silme olayları ve audit, yetki/IDOR; bakım işi (7 gün <c>Expired</c>, 30 gün temizlik zaman penceresi,
/// temizlenmiş partide geri alma). Sentetik veri; işçiler testte doğrudan çalıştırılır.
/// </summary>
[Collection("imports-api")]
public sealed class ImportRollbackEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] AdminPermissions =
    [
        "Imports.Read", "Imports.Manage", "Imports.Commit", "Clients.Create", "Pets.Create", "Vaccinations.Create",
    ];

    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly CustomWebApplicationFactory _factory;

    public ImportRollbackEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    // ================================================================ Önizleme + tam geri alma

    [Fact]
    public async Task Preview_And_Rollback_Should_Delete_Only_Batch_Records_And_Keep_Matched()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var (id, validationId) = await UploadValidatedScenarioAsync(client, ctx);

        // M1 önizlemeden sonra elle eklendi: uygulamada eşleşir (Matched) ve geri almada asla silinmez.
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

        var preview = await GetJsonAsync(client, $"/api/v1/imports/batches/{id}/rollback-preview");
        preview.GetProperty("status").GetString().Should().Be("Committed");
        Counts(preview, "deletable").Should().Equal(1, 2, 3); // clients, pets, vaccinations
        Counts(preview, "blocked").Should().Equal(0, 0, 0);
        preview.GetProperty("canRollback").GetBoolean().Should().BeTrue();
        preview.GetProperty("canDeleteUnblockedOnly").GetBoolean().Should().BeFalse();
        preview.GetProperty("kept").GetProperty("matchedClients").GetInt32().Should().Be(1);
        preview.GetProperty("blockedItems").GetArrayLength().Should().Be(0);
        preview.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).Should().Equal("Imports.PetEditsNotTracked");

        var requestId = Guid.NewGuid();
        var response = await PostRollbackAsync(client, id, requestId, "Yanlis dosya yuklendi");
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().Should().Be("RollingBack");

        var before = await GetJsonAsync(client, $"/api/v1/imports/batches/{id}/progress");
        before.GetProperty("phase").GetString().Should().Be("RollingBack");
        before.GetProperty("processed").GetInt32().Should().Be(0);
        before.GetProperty("total").GetInt32().Should().Be(6);

        await RunRollbackWorkerAsync();

        var after = await GetJsonAsync(client, $"/api/v1/imports/batches/{id}/progress");
        after.GetProperty("status").GetString().Should().Be("RolledBack");
        after.GetProperty("processed").GetInt32().Should().Be(6);
        after.GetProperty("rolledBackAtUtc").ValueKind.Should().Be(JsonValueKind.String);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Clients.Where(c => c.TenantId == ctx.TenantId).Select(c => c.Id).ToListAsync())
                .Should().Equal([manualId], "yalnız eşleşen mevcut müşteri kalır");
            (await db.Pets.CountAsync(p => p.TenantId == ctx.TenantId)).Should().Be(0);
            (await db.Vaccinations.CountAsync(v => v.TenantId == ctx.TenantId)).Should().Be(0);

            var rows = await db.ImportBatchRows.AsNoTracking().Where(r => r.BatchId == id).ToListAsync();
            rows.Count(r => r.ApplyStatus == ImportRowApplyStatus.RolledBack).Should().Be(6);
            rows.Single(r => r.Table == ImportTable.Customers && r.RowNumber == 2).ApplyStatus
                .Should().Be(ImportRowApplyStatus.Matched, "eşleşen satır geri almada değişmez");

            // Silme olayları silmeyle aynı işlemde (Query DB hayalet kayıt bırakmasın).
            var petIds = rows.Where(r => r.Table == ImportTable.Pets && r.ApplyStatus == ImportRowApplyStatus.RolledBack)
                .Select(r => r.TargetEntityId!.Value.ToString()).ToList();
            var petEvents = await db.OutboxMessages.AsNoTracking()
                .Where(m => m.Type == PetIntegrationEventTypes.Deleted).Select(m => m.Payload).ToListAsync();
            petIds.Should().OnlyContain(pid => petEvents.Count(p => p.Contains(pid, StringComparison.OrdinalIgnoreCase)) == 1);
            var m2 = rows.Single(r => r.Table == ImportTable.Customers && r.RowNumber == 3).TargetEntityId!.Value.ToString();
            (await db.OutboxMessages.AsNoTracking().Where(m => m.Type == ClientIntegrationEventTypes.Deleted).Select(m => m.Payload).ToListAsync())
                .Count(p => p.Contains(m2, StringComparison.OrdinalIgnoreCase)).Should().Be(1);
            (await db.OutboxMessages.AsNoTracking().Where(m => m.Type == ClientIntegrationEventTypes.Deleted).Select(m => m.Payload).ToListAsync())
                .Should().NotContain(p => p.Contains(manualId.ToString(), StringComparison.OrdinalIgnoreCase));

            // Audit: istek gerekçesi ve iş sonucu sayıları; satır verisi (ad) yok.
            var audits = await db.AuditLogs.AsNoTracking()
                .Where(a => a.TargetId == $"ImportBatchId={id}" && (a.Action == "ImportBatch.Rollback" || a.Action == "ImportBatch.RollbackCompleted"))
                .ToListAsync();
            audits.Select(a => a.Action).Should().BeEquivalentTo("ImportBatch.Rollback", "ImportBatch.RollbackCompleted");
            audits.Single(a => a.Action == "ImportBatch.Rollback").RequestPayload.Should().Contain("Yanlis dosya yuklendi").And.Contain(requestId.ToString());
            var completed = audits.Single(a => a.Action == "ImportBatch.RollbackCompleted");
            completed.Success.Should().BeTrue();
            completed.RequestPayload.Should().Contain("\"deleted\":2"); // pets veya vaccinations sayısı
            audits.Should().OnlyContain(a => a.RequestPayload == null || !a.RequestPayload.Contains(ctx.Suffix, StringComparison.Ordinal));
        }

        var done = await GetJsonAsync(client, $"/api/v1/imports/batches/{id}/rollback-preview");
        Counts(done, "rolledBack").Should().Equal(1, 2, 3);
        Counts(done, "deletable").Should().Equal(0, 0, 0);
        done.GetProperty("canRollback").GetBoolean().Should().BeFalse("RolledBack son durumdur");

        var replay = await PostRollbackAsync(client, id, requestId, "Yanlis dosya yuklendi");
        replay.StatusCode.Should().Be(HttpStatusCode.OK, "aynı requestId tekrar: yeni iş başlamaz");
        (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().Should().Be("RolledBack");
    }

    // ================================================================ K7: engel, DeleteUnblockedOnly, yeniden deneme

    [Fact]
    public async Task Fail_Policy_Should_Refuse_With_Dependencies_And_DeleteUnblockedOnly_Should_Keep_Them_Then_Retry()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var (id, validationId) = await UploadValidatedScenarioAsync(client, ctx);
        await CommitAndRunAsync(client, id, validationId);
        var targets = await TargetsAsync(id);

        // Pamuk (H1) için geliş açıldı; M2 müşterisi sonradan düzenlendi.
        Guid visitId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var visit = new Visit(ctx.TenantId, ctx.ClinicId, targets[(ImportTable.Pets, 2)], null, null, Guid.NewGuid(), DateTime.UtcNow);
            db.Visits.Add(visit);
            var m2Id = targets[(ImportTable.Customers, 3)];
            var m2 = await db.Clients.SingleAsync(c => c.Id == m2Id);
            m2.UpdateDetails(m2.FullName, "veli@example.com", null, null, DateTime.UtcNow);
            await db.SaveChangesAsync();
            visitId = visit.Id;
        }

        var preview = await GetJsonAsync(client, $"/api/v1/imports/batches/{id}/rollback-preview");
        Counts(preview, "deletable").Should().Equal(0, 1, 3);
        Counts(preview, "blocked").Should().Equal(2, 1, 0);
        preview.GetProperty("canRollback").GetBoolean().Should().BeFalse();
        preview.GetProperty("canDeleteUnblockedOnly").GetBoolean().Should().BeTrue();
        var items = preview.GetProperty("blockedItems").EnumerateArray().ToList();
        var pet = items.Single(i => i.GetProperty("entity").GetString() == "Pet");
        pet.GetProperty("table").GetString().Should().Be("pets");
        pet.GetProperty("rowNumber").GetInt32().Should().Be(2);
        pet.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()).Should().Equal(ImportRollbackReasons.HasVisits);
        items.Single(i => i.GetProperty("id").GetGuid() == targets[(ImportTable.Customers, 3)])
            .GetProperty("reasons").EnumerateArray().Select(r => r.GetString()).Should().Equal(ImportRollbackReasons.ClientModified);
        items.Single(i => i.GetProperty("id").GetGuid() == targets[(ImportTable.Customers, 2)])
            .GetProperty("reasons").EnumerateArray().Select(r => r.GetString()).Should().Equal(ImportRollbackReasons.HasKeptPets);

        // Varsayılan Fail: 409 + liste, hiçbir şey silinmez.
        var refused = await PostRollbackAsync(client, id, Guid.NewGuid(), "Deneme");
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("Imports.RollbackBlocked");
        problem.GetProperty("blocked").GetProperty("pets").GetInt32().Should().Be(1);
        problem.GetProperty("blockedItems").GetArrayLength().Should().Be(3);
        (await StatusAsync(id)).Should().Be(ImportBatchStatus.Committed);
        (await CountsInTenantAsync(ctx.TenantId)).Should().Be((2, 2, 3));

        // DeleteUnblockedOnly: bağı olmayanlar silinir, olanlar kalır.
        var partial = await PostRollbackAsync(client, id, Guid.NewGuid(), "Kısmi geri alma", "DeleteUnblockedOnly");
        partial.StatusCode.Should().Be(HttpStatusCode.Accepted, await partial.Content.ReadAsStringAsync());
        await RunRollbackWorkerAsync();
        (await StatusAsync(id)).Should().Be(ImportBatchStatus.PartiallyRolledBack);
        (await CountsInTenantAsync(ctx.TenantId)).Should().Be((2, 1, 0), "M1, M2 ve geliş açılmış Pamuk kalır; aşılar ve Kehribar silinir");

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var kept = await db.ImportBatchRows.AsNoTracking()
                .Where(r => r.BatchId == id && r.ApplyStatus == ImportRowApplyStatus.Kept).ToListAsync();
            kept.Should().HaveCount(3);
            kept.Single(r => r.Table == ImportTable.Pets).IssuesJson.Should().Contain("Imports.RollbackKept").And.Contain("HasVisits");
        }

        var rows = await GetJsonAsync(client, $"/api/v1/imports/batches/{id}/rows?status=Kept&pageSize=200");
        rows.GetProperty("totalItems").GetInt32().Should().Be(3);

        // Yeniden deneme (yeni requestId): engel sürdükçe Fail yine reddedilir; geliş kalkınca kalanlar silinir.
        (await PostRollbackAsync(client, id, Guid.NewGuid(), "Tekrar")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Visits.Where(v => v.Id == visitId).ExecuteDeleteAsync();
        }

        var retry = await PostRollbackAsync(client, id, Guid.NewGuid(), "Geliş iptal edildi", "DeleteUnblockedOnly");
        retry.StatusCode.Should().Be(HttpStatusCode.Accepted, await retry.Content.ReadAsStringAsync());
        await RunRollbackWorkerAsync();
        (await StatusAsync(id)).Should().Be(ImportBatchStatus.PartiallyRolledBack, "M2 düzenlendiği için kalır");
        (await CountsInTenantAsync(ctx.TenantId)).Should().Be((1, 0, 0));
        var final = await GetJsonAsync(client, $"/api/v1/imports/batches/{id}/rollback-preview");
        Counts(final, "blocked").Should().Equal(1, 0, 0);
        Counts(final, "rolledBack").Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Fail_Policy_Job_Should_Delete_Nothing_When_Dependency_Appears_After_Request()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var (id, validationId) = await UploadValidatedScenarioAsync(client, ctx);
        await CommitAndRunAsync(client, id, validationId);
        var targets = await TargetsAsync(id);

        (await PostRollbackAsync(client, id, Guid.NewGuid(), "Yanlış parti")).StatusCode.Should().Be(HttpStatusCode.Accepted);

        // İstekten sonra, iş başlamadan geliş açıldı.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Visits.Add(new Visit(ctx.TenantId, ctx.ClinicId, targets[(ImportTable.Pets, 3)], null, null, Guid.NewGuid(), DateTime.UtcNow));
            await db.SaveChangesAsync();
        }

        await RunRollbackWorkerAsync();

        (await StatusAsync(id)).Should().Be(ImportBatchStatus.RollbackFailed);
        (await CountsInTenantAsync(ctx.TenantId)).Should().Be((2, 2, 3), "Fail politikasında hiçbir şey silinmez");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var audit = await db.AuditLogs.AsNoTracking()
                .SingleAsync(a => a.TargetId == $"ImportBatchId={id}" && a.Action == "ImportBatch.RollbackCompleted");
            audit.Success.Should().BeFalse();
            audit.FailureReason.Should().Be("Imports.RollbackBlocked");
        }

        // RollbackFailed yeniden denenebilir.
        (await PostRollbackAsync(client, id, Guid.NewGuid(), "Kısmi", "DeleteUnblockedOnly")).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await RunRollbackWorkerAsync();
        (await StatusAsync(id)).Should().Be(ImportBatchStatus.PartiallyRolledBack);
        (await CountsInTenantAsync(ctx.TenantId)).Should().Be((1, 1, 0), "yalnız gelişli Kehribar ve sahibi M2 kalır");
    }

    // ================================================================ Idempotency, doğrulama, eşzamanlılık

    [Fact]
    public async Task Rollback_Requests_Should_Be_Validated_Idempotent_And_Concurrency_Safe()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var (id, validationId) = await UploadValidatedScenarioAsync(client, ctx);
        await CommitAndRunAsync(client, id, validationId);

        (await client.PostAsJsonAsync($"/api/v1/imports/batches/{id}/rollback", new { requestId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "gerekçe zorunlu");
        (await client.PostAsJsonAsync($"/api/v1/imports/batches/{id}/rollback", new { requestId = Guid.NewGuid(), reason = new string('x', 501) }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync($"/api/v1/imports/batches/{id}/rollback", new { requestId = Guid.NewGuid(), reason = "x", blockedPolicy = "KeepBlocked" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync($"/api/v1/imports/batches/{id}/rollback", new { reason = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "requestId zorunlu");
        (await StatusAsync(id)).Should().Be(ImportBatchStatus.Committed);

        // Aynı requestId ile eşzamanlı iki istek: tek iş.
        var requestId = Guid.NewGuid();
        var responses = await Task.WhenAll(
            PostRollbackAsync(client, id, requestId, "Eşzamanlı"),
            PostRollbackAsync(ClientFor(ctx.Token), id, requestId, "Eşzamanlı"));
        responses.Select(r => r.StatusCode).Should().BeEquivalentTo(new[] { HttpStatusCode.Accepted, HttpStatusCode.OK });

        var conflict = await PostRollbackAsync(client, id, requestId, "Eşzamanlı", "DeleteUnblockedOnly");
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(conflict)).Should().Be("Imports.RequestIdConflict");
        var other = await PostRollbackAsync(client, id, Guid.NewGuid(), "Başka istek");
        other.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(other)).Should().Be("Imports.InvalidState");

        await RunRollbackWorkerAsync();
        (await StatusAsync(id)).Should().Be(ImportBatchStatus.RolledBack);
        (await PostRollbackAsync(client, id, requestId, "Eşzamanlı")).StatusCode.Should().Be(HttpStatusCode.OK);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.AuditLogs.CountAsync(a => a.TargetId == $"ImportBatchId={id}" && a.Action == "ImportBatch.RollbackCompleted"))
                .Should().Be(1, "tek iş çalıştı");
        }

        // Farklı requestId'li eşzamanlı iki istek: yalnız biri başlatır.
        var (second, secondValidation) = await UploadCustomersAsync(client, ctx, 2);
        await CommitAndRunAsync(client, second, secondValidation);
        var race = await Task.WhenAll(
            PostRollbackAsync(client, second, Guid.NewGuid(), "A"),
            PostRollbackAsync(ClientFor(ctx.Token), second, Guid.NewGuid(), "B"));
        race.Count(r => r.StatusCode == HttpStatusCode.Accepted).Should().Be(1);
        race.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
        await RunRollbackWorkerAsync();
        (await StatusAsync(second)).Should().Be(ImportBatchStatus.RolledBack);

        // Kiracıda geri alma sürerken başka iş (uygulama, başka partinin geri alması) başlamaz.
        var (third, thirdValidation) = await UploadCustomersAsync(client, ctx, 1);
        await CommitAndRunAsync(client, third, thirdValidation);
        var (fourth, fourthValidation) = await UploadCustomersAsync(client, ctx, 1);
        await CommitAndRunAsync(client, fourth, fourthValidation);
        var (fifth, fifthValidation) = await UploadCustomersAsync(client, ctx, 1);

        (await PostRollbackAsync(client, third, Guid.NewGuid(), "C")).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var busy = await PostRollbackAsync(client, fourth, Guid.NewGuid(), "D");
        busy.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(busy)).Should().Be("Imports.AnotherJobRunning");
        var commitBusy = await client.PostAsJsonAsync($"/api/v1/imports/batches/{fifth}/commit", new { validationId = fifthValidation });
        (await CodeAsync(commitBusy)).Should().Be("Imports.AnotherJobRunning");

        await RunRollbackWorkerAsync();
        (await StatusAsync(third)).Should().Be(ImportBatchStatus.RolledBack);
        (await StatusAsync(fourth)).Should().Be(ImportBatchStatus.Committed);
    }

    // ================================================================ Çökme, devralma, kira çiti

    [Fact]
    public async Task Crashed_Rollback_Worker_Should_Be_Taken_Over_Without_Double_Deletes()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var (id, validationId) = await UploadValidatedScenarioAsync(client, ctx);
        await CommitAndRunAsync(client, id, validationId);
        (await PostRollbackAsync(client, id, Guid.NewGuid(), "Çökme provası")).StatusCode.Should().Be(HttpStatusCode.Accepted);

        // İşçi A partiyi aldı, ilk sahip birimini geri aldı ve çöktü (kira hâlâ geçerli).
        const string crashed = "w-rb-crashed";
        await MutateBatchAsync(id, b => b.ClaimRollbackLease(crashed, DateTime.UtcNow).IsSuccess.Should().BeTrue());
        var plan = await PlanAsync(ctx.TenantId, id);
        plan.Units.Should().HaveCount(2);
        var first = await SendUnitAsync(ctx.TenantId, id, crashed, plan.Units[0]);
        first.IsSuccess.Should().BeTrue();
        first.Value!.Deleted.Should().Be(4, "M1 + Pamuk + iki aşı");

        var takeover = RollbackProcessor("w-rb-takeover");
        (await takeover.ProcessNextAsync(CancellationToken.None)).Should().BeFalse("kira dolmadan devralınmaz");
        var progress = await GetJsonAsync(client, $"/api/v1/imports/batches/{id}/progress");
        progress.GetProperty("leaseActive").GetBoolean().Should().BeTrue();
        progress.GetProperty("processed").GetInt32().Should().Be(4);

        await MutateBatchAsync(id, b => b.ClaimRollbackLease(crashed, DateTime.UtcNow - ImportBatch.RollbackLeaseDuration - TimeSpan.FromSeconds(1)));
        (await takeover.ProcessNextAsync(CancellationToken.None)).Should().BeTrue();

        (await StatusAsync(id)).Should().Be(ImportBatchStatus.RolledBack);
        (await CountsInTenantAsync(ctx.TenantId)).Should().Be((0, 0, 0));

        // Kira çiti: eski işçinin geç kalan birimi reddedilir.
        var late = await SendUnitAsync(ctx.TenantId, id, crashed, plan.Units[1]);
        late.Error.Code.Should().Be(ImportOwnerUnitErrors.LeaseLostCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var m1 = (await TargetsAsync(id))[(ImportTable.Customers, 2)].ToString();
        (await db.OutboxMessages.AsNoTracking().Where(m => m.Type == ClientIntegrationEventTypes.Deleted).Select(m => m.Payload).ToListAsync())
            .Count(p => p.Contains(m1, StringComparison.OrdinalIgnoreCase)).Should().Be(1, "aynı birim iki kez geri alınmaz");
    }

    // ================================================================ Yetki / IDOR / durum

    [Fact]
    public async Task Rollback_Endpoints_Should_Enforce_Permissions_State_And_Tenant_Isolation()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var (id, validationId) = await UploadValidatedScenarioAsync(client, ctx);

        var early = await client.GetAsync($"/api/v1/imports/batches/{id}/rollback-preview");
        early.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(early)).Should().Be("Imports.InvalidState");
        var earlyRollback = await PostRollbackAsync(client, id, Guid.NewGuid(), "Erken");
        earlyRollback.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(earlyRollback)).Should().Be("Imports.InvalidState");

        await CommitAndRunAsync(client, id, validationId);

        var readOnly = ClientFor(await TokenAsync(ctx, ["Imports.Read"]));
        (await readOnly.GetAsync($"/api/v1/imports/batches/{id}/rollback-preview")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await PostRollbackAsync(readOnly, id, Guid.NewGuid(), "x")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var withoutPets = ClientFor(await TokenAsync(ctx, ["Imports.Read", "Imports.Commit", "Clients.Create", "Vaccinations.Create"]));
        var denied = await PostRollbackAsync(withoutPets, id, Guid.NewGuid(), "x");
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(denied)).Should().Be("Imports.AccessDenied");

        var other = ClientFor((await SeedAsync()).Token);
        (await other.GetAsync($"/api/v1/imports/batches/{id}/rollback-preview")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PostRollbackAsync(other, id, Guid.NewGuid(), "x")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await StatusAsync(id)).Should().Be(ImportBatchStatus.Committed);
        (await CountsInTenantAsync(ctx.TenantId)).Should().Be((2, 2, 3));
    }

    // ================================================================ Bakım işi (K9)

    [Fact]
    public async Task Maintenance_Should_Expire_Idle_Draft_After_7_Days_And_Delete_Its_Rows()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var (id, _) = await UploadCustomersAsync(client, ctx, 3);
        var now = DateTime.UtcNow;

        await MaintenanceAsync(now.AddDays(6).AddHours(23));
        (await StatusAsync(id)).Should().Be(ImportBatchStatus.Validated, "7 gün dolmadı");

        var result = await MaintenanceAsync(now.AddDays(7).AddMinutes(5));
        result.ExpiredBatches.Should().BeGreaterThanOrEqualTo(1);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var batch = await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == id);
        batch.Status.Should().Be(ImportBatchStatus.Expired);
        batch.PayloadPurgedAtUtc.Should().NotBeNull();
        (await db.ImportBatchRows.CountAsync(r => r.BatchId == id)).Should().Be(0, "kişisel veri süre dolarken silinir");
        (await db.AuditLogs.CountAsync(a => a.TargetId == $"ImportBatchId={id}" && a.Action == "ImportBatch.Expired")).Should().Be(1);
        (await client.GetAsync($"/api/v1/imports/batches/{id}/rows")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Maintenance_Should_Purge_Payload_Only_After_30_Days_And_Rollback_Should_Still_Work()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var (id, validationId) = await UploadValidatedScenarioAsync(client, ctx);
        await CommitAndRunAsync(client, id, validationId);
        var committedAt = DateTime.UtcNow;

        await MaintenanceAsync(committedAt.AddDays(29));
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == id)).PayloadPurgedAtUtc.Should().BeNull("30 gün dolmadı");
            (await db.ImportBatchRows.CountAsync(r => r.BatchId == id && r.SourceJson != null)).Should().Be(13);
        }

        await MaintenanceAsync(committedAt.AddDays(30).AddMinutes(5));

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == id)).PayloadPurgedAtUtc.Should().NotBeNull();
            var rows = await db.ImportBatchRows.AsNoTracking().Where(r => r.BatchId == id).ToListAsync();
            rows.Should().HaveCount(13, "satırların kendisi ve sonuçları kalır");
            rows.Should().OnlyContain(r => r.SourceJson == null && r.NormalizedJson == null && r.IssuesJson == null);
            rows.Count(r => r.ApplyStatus == ImportRowApplyStatus.Created && r.TargetEntityId != null).Should().Be(7);
            (await db.AuditLogs.CountAsync(a => a.TargetId == $"ImportBatchId={id}" && a.Action == "ImportBatch.PayloadPurged")).Should().Be(1);
        }

        var rowsResponse = await client.GetAsync($"/api/v1/imports/batches/{id}/rows");
        rowsResponse.StatusCode.Should().Be(HttpStatusCode.Gone);
        (await CodeAsync(rowsResponse)).Should().Be("Imports.PayloadPurged");
        (await client.GetAsync($"/api/v1/imports/batches/{id}/errors")).StatusCode.Should().Be(HttpStatusCode.Gone);
        (await GetJsonAsync(client, $"/api/v1/imports/batches/{id}/preview")).GetProperty("payloadPurged").GetBoolean().Should().BeTrue();

        // Geri alma satır verisine değil hedef kimliklere dayanır: temizlenmiş partide de çalışır.
        Counts(await GetJsonAsync(client, $"/api/v1/imports/batches/{id}/rollback-preview"), "deletable").Should().Equal(2, 2, 3);
        (await PostRollbackAsync(client, id, Guid.NewGuid(), "30 gün sonra geri alma")).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await RunRollbackWorkerAsync();
        (await StatusAsync(id)).Should().Be(ImportBatchStatus.RolledBack);
        (await CountsInTenantAsync(ctx.TenantId)).Should().Be((0, 0, 0));
    }

    // ================================================================ Yardımcılar

    private static int[] Counts(JsonElement json, string property)
    {
        var c = json.GetProperty(property);
        return new[] { "clients", "pets", "vaccinations" }.Select(k => c.GetProperty(k).GetInt32()).ToArray();
    }

    private static Task<HttpResponseMessage> PostRollbackAsync(
        HttpClient client, Guid id, Guid requestId, string reason, string? blockedPolicy = null)
        => client.PostAsJsonAsync($"/api/v1/imports/batches/{id}/rollback", new { requestId, reason, blockedPolicy });

    private ImportRollbackProcessor RollbackProcessor(string workerId)
        => new(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ImportCommitOptions { Backpressure = new ImportBackpressureOptions { Enabled = false } }),
            TimeProvider.System,
            NullLogger<ImportRollbackProcessor>.Instance,
            workerId);

    private async Task RunRollbackWorkerAsync()
    {
        var processor = RollbackProcessor($"w-rb-{Guid.NewGuid():N}"[..20]);
        for (var i = 0; i < 20 && await processor.ProcessNextAsync(CancellationToken.None); i++)
        {
        }
    }

    private async Task RunCommitWorkerAsync()
    {
        var processor = new ImportCommitProcessor(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ImportCommitOptions { Backpressure = new ImportBackpressureOptions { Enabled = false } }),
            TimeProvider.System,
            NullLogger<ImportCommitProcessor>.Instance,
            $"w-c-{Guid.NewGuid():N}"[..20]);
        for (var i = 0; i < 20 && await processor.ProcessNextAsync(CancellationToken.None); i++)
        {
        }
    }

    /// <summary>Bakım turu, verilen "şimdi" ile (zaman penceresi); tüm uygun partiler tek turda.</summary>
    private async Task<ImportMaintenanceResult> MaintenanceAsync(DateTime nowUtc)
    {
        var processor = new ImportMaintenanceProcessor(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ImportMaintenanceOptions { BatchSize = 100_000 }),
            new FakeTimeProvider(nowUtc),
            NullLogger<ImportMaintenanceProcessor>.Instance);
        return await processor.RunOnceAsync(CancellationToken.None);
    }

    private async Task CommitAndRunAsync(HttpClient client, Guid id, Guid validationId)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/imports/batches/{id}/commit", new { validationId });
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        await RunCommitWorkerAsync();
        (await StatusAsync(id)).Should().Be(ImportBatchStatus.Committed);
    }

    private async Task<ImportRollbackPlan> PlanAsync(Guid tenantId, Guid id)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        using var tenant = BackgroundTenantScope.Begin(tenantId);
        return (await scope.ServiceProvider.GetRequiredService<ImportRollbackPlanLoader>().LoadAsync(tenantId, id, null, CancellationToken.None)).Plan;
    }

    private async Task<Backend.Veteriner.Domain.Shared.Result<ImportRollbackUnitResult>> SendUnitAsync(
        Guid tenantId, Guid batchId, string worker, ImportRollbackUnit unit)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        using var tenant = BackgroundTenantScope.Begin(tenantId);
        try
        {
            return await scope.ServiceProvider.GetRequiredService<ISender>()
                .Send(new RollbackImportOwnerUnitCommand(batchId, worker, unit.RowIds, ImportRollbackPolicy.Fail));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Backend.Veteriner.Domain.Shared.Result<ImportRollbackUnitResult>.Failure(ImportOwnerUnitErrors.LeaseLost());
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

    /// <summary>(tablo, satır no) → oluşturulan kaydın kimliği.</summary>
    private async Task<Dictionary<(ImportTable, int), Guid>> TargetsAsync(Guid id)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ImportBatchRows.AsNoTracking()
            .Where(r => r.BatchId == id && r.TargetEntityId != null
                        && (r.ApplyStatus == ImportRowApplyStatus.Created || r.ApplyStatus == ImportRowApplyStatus.Kept
                            || r.ApplyStatus == ImportRowApplyStatus.RolledBack))
            .ToDictionaryAsync(r => (r.Table, r.RowNumber), r => r.TargetEntityId!.Value);
    }

    private async Task<(int Clients, int Pets, int Vaccinations)> CountsInTenantAsync(Guid tenantId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (
            await db.Clients.CountAsync(c => c.TenantId == tenantId),
            await db.Pets.CountAsync(p => p.TenantId == tenantId),
            await db.Vaccinations.CountAsync(v => v.TenantId == tenantId));
    }

    /// <summary>
    /// B4 senaryosu: 4 müşteri / 4 hasta / 5 aşı (13 satır). Uygulama sonrası oluşan: M1 (satır 2) + Pamuk (H1, satır 2) +
    /// 2 aşı; M2 (satır 3) + Kehribar (H2, satır 3) + 1 aşı. Hatalı/mükerrer satırlar Skipped.
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
                ["MusteriNo", "HayvanNo", "HayvanAdi", "Tur"],
                ["M1", "H1", "Pamuk", ctx.SpeciesName],
                ["M2", "H2", "Kehribar", ctx.SpeciesName],
                ["M3", "H4", "Hatalı", ctx.SpeciesName],
                ["M4", "H5", "Pamuk", ctx.SpeciesName],
            ]),
            ("Asilar",
            [
                ["HayvanNo", "AsiAdi", "UygulamaTarihi", "SonrakiAsiTarihi"],
                ["H1", ctx.VaccineName, "10.04.2024", today.AddDays(2).ToString("dd.MM.yyyy")],
                ["H1", ctx.VaccineName, "10.04.2023", "10.04.2024"],
                ["H2", ctx.VaccineName, "10.04.2024", today.AddDays(30).ToString("dd.MM.yyyy")],
                ["H1", ctx.VaccineName, "10.04.2024", null],
                ["H4", ctx.VaccineName, "10.04.2024", null],
            ]));

        var id = await UploadAsync(client, ctx, bytes);
        return (id, await ValidateAsync(client, id));
    }

    private async Task<(Guid Id, Guid ValidationId)> UploadCustomersAsync(HttpClient client, SeedCtx ctx, int count)
    {
        var rows = new List<object?[]> { new object?[] { "AdSoyad", "Telefon" } };
        for (var i = 0; i < count; i++)
            rows.Add([$"Geri {Guid.NewGuid():N}"[..20], $"0532{Random.Shared.Next(1000000, 9999999)}"]);
        var id = await UploadAsync(client, ctx, Workbook(("Musteriler", rows.ToArray())));
        return (id, await ValidateAsync(client, id));
    }

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
    private async Task<SeedCtx> SeedAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var tenant = new Tenant($"imr-{Guid.NewGuid():N}"[..18], DateTime.UtcNow);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        var clinic = new Clinic(tenant.Id, $"Imr-{Guid.NewGuid():N}"[..14], "Istanbul");
        db.Clinics.Add(clinic);
        var species = new Species($"IMR{suffix}", $"Imr Tür {suffix}");
        var vaccine = VaccineDefinition.CreateTenant(tenant.Id, $"IMRV{suffix}", $"Imr Aşı {suffix}", DateTime.UtcNow);
        db.Species.Add(species);
        db.VaccineDefinitions.Add(vaccine);
        await db.SaveChangesAsync();

        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);
        var token = await IntegrationTestAuthHelper.SeedScopedClinicUserAndIssueTokenAsync(
            db, jwt, hasher, tenant.Id, clinic.Id, AdminPermissions);

        return new SeedCtx(tenant.Id, clinic.Id, token, suffix, species.Name, vaccine.Name, $"Ayşe {suffix}");
    }

    private sealed record SeedCtx(
        Guid TenantId, Guid ClinicId, string Token, string Suffix, string SpeciesName, string VaccineName, string CustomerName);
}
