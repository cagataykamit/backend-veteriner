using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Imports;

/// <summary>
/// İçe aktarma B1 (gerçek SQL Server/LocalDB): parti iskeleti uçları, requestId tekrarı, taslak sınırı, kiracı/klinik
/// IDOR, durum geçişi çakışmaları ve veritabanı düzeyi idempotency/eşzamanlılık indeksleri.
/// </summary>
[Collection("imports-api")]
public sealed class ImportBatchesEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] ImportPermissions = { "Imports.Read", "Imports.Manage" };

    private readonly CustomWebApplicationFactory _factory;

    public ImportBatchesEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Create_Should_Return_Uploaded_Batch_Readable_By_Id_And_List()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var requestId = Guid.NewGuid();

        var response = await PostAsync(client, requestId, "  Eski sistem 1  ", ctx.ClinicId);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        created.GetProperty("status").GetString().Should().Be("Uploaded");
        created.GetProperty("name").GetString().Should().Be("Eski sistem 1");
        created.GetProperty("clinicId").GetGuid().Should().Be(ctx.ClinicId);
        created.GetProperty("validationId").ValueKind.Should().Be(JsonValueKind.Null);
        created.GetProperty("summary").GetProperty("vaccinations").GetProperty("total").GetInt32().Should().Be(0);

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/imports/batches/{id}");
        detail.GetProperty("status").GetString().Should().Be("Uploaded");

        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/imports/batches?status=Uploaded");
        list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Equal(id);
        list.GetProperty("totalItems").GetInt32().Should().Be(1);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "ImportBatch.Create" && a.TargetId != null && a.TargetId.Contains(requestId.ToString()))
            .FirstOrDefaultAsync();
        audit.Should().NotBeNull();
        audit!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Same_RequestId_Should_Replay_Same_Batch_And_Reject_Different_Content()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var requestId = Guid.NewGuid();

        var first = await PostAsync(client, requestId, "Parti A", null);
        var replay = await PostAsync(client, requestId, "Parti A", null);
        var conflict = await PostAsync(client, requestId, "Parti B", null);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await IdAsync(replay)).Should().Be(await IdAsync(first));
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(conflict)).Should().Be("Imports.RequestIdConflict");
        (await BatchCountAsync(ctx.TenantId)).Should().Be(1);
    }

    [Fact]
    public async Task Create_Without_RequestId_Should_Be_Bad_Request()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);

        var response = await client.PostAsync(
            "/api/v1/imports/batches",
            new MultipartFormDataContent { { new StringContent("Parti"), "name" } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await BatchCountAsync(ctx.TenantId)).Should().Be(0);
    }

    [Fact]
    public async Task Sixth_Open_Draft_Should_Be_Rejected_Until_One_Is_Discarded()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var ids = new List<Guid>();
        for (var i = 0; i < ImportBatch.MaxOpenDraftsPerTenant; i++)
            ids.Add(await IdAsync(await PostAsync(client, Guid.NewGuid(), $"Parti {i}", null)));

        var rejected = await PostAsync(client, Guid.NewGuid(), "Fazla", null);
        rejected.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(rejected)).Should().Be("Imports.TooManyOpenDrafts");

        (await client.DeleteAsync($"/api/v1/imports/batches/{ids[0]}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await PostAsync(client, Guid.NewGuid(), "Yer açıldı", null)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Discard_Should_Mark_Discarded_And_Second_Discard_Should_Conflict()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var id = await IdAsync(await PostAsync(client, Guid.NewGuid(), "Silinecek", null));

        var deleted = await client.DeleteAsync($"/api/v1/imports/batches/{id}");
        var again = await client.DeleteAsync($"/api/v1/imports/batches/{id}");

        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/imports/batches/{id}");
        detail.GetProperty("status").GetString().Should().Be("Discarded");
        detail.GetProperty("updatedAtUtc").ValueKind.Should().NotBe(JsonValueKind.Null);
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(again)).Should().Be("Imports.InvalidState");
    }

    [Fact]
    public async Task Committed_Batch_Should_Not_Be_Discarded()
    {
        var ctx = await SeedAsync();
        var batchId = await SeedBatchAsync(ctx.TenantId, null, b =>
        {
            var validationId = Guid.NewGuid();
            b.ApplyMapping("{}", null, DateTime.UtcNow);
            b.StartValidation(DateTime.UtcNow);
            b.CompleteValidation(validationId, ImportTableCounts.Empty, ImportTableCounts.Empty, ImportTableCounts.Empty, DateTime.UtcNow);
            b.StartCommit(validationId, DateTime.UtcNow);
            b.CompleteCommit(ImportTableCounts.Empty, ImportTableCounts.Empty, ImportTableCounts.Empty, DateTime.UtcNow);
        });

        var response = await ClientFor(ctx.Token).DeleteAsync($"/api/v1/imports/batches/{batchId}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(response)).Should().Be("Imports.InvalidState");
        (await StatusAsync(batchId)).Should().Be(ImportBatchStatus.Committed);
    }

    [Fact]
    public async Task Other_Tenants_Batch_Should_Be_Not_Found_And_Not_Listed()
    {
        var ctx = await SeedAsync();
        var foreign = await SeedAsync();
        var foreignBatchId = await IdAsync(await PostAsync(ClientFor(foreign.Token), Guid.NewGuid(), "Yabancı", null));
        var client = ClientFor(ctx.Token);

        var get = await client.GetAsync($"/api/v1/imports/batches/{foreignBatchId}");
        var delete = await client.DeleteAsync($"/api/v1/imports/batches/{foreignBatchId}");
        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/imports/batches");

        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(get)).Should().Be("Imports.BatchNotFound");
        delete.StatusCode.Should().Be(HttpStatusCode.NotFound);
        list.GetProperty("totalItems").GetInt32().Should().Be(0);
        (await StatusAsync(foreignBatchId)).Should().Be(ImportBatchStatus.Uploaded);
    }

    [Fact]
    public async Task Batch_Bound_To_Unassigned_Clinic_Should_Be_Hidden_From_Clinic_Scoped_User()
    {
        var ctx = await SeedAsync();
        var otherClinicId = await SeedClinicAsync(ctx.TenantId);
        var hiddenId = await SeedBatchAsync(ctx.TenantId, otherClinicId, _ => { });
        var tenantWideId = await SeedBatchAsync(ctx.TenantId, null, _ => { });
        var client = ClientFor(ctx.Token);

        var get = await client.GetAsync($"/api/v1/imports/batches/{hiddenId}");
        var delete = await client.DeleteAsync($"/api/v1/imports/batches/{hiddenId}");
        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/imports/batches");
        var create = await PostAsync(client, Guid.NewGuid(), "Başka klinik", otherClinicId);

        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
        delete.StatusCode.Should().Be(HttpStatusCode.NotFound);
        list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Equal(tenantWideId);
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(create)).Should().Be("Clinics.AccessDenied");
        (await StatusAsync(hiddenId)).Should().Be(ImportBatchStatus.Uploaded);
    }

    [Fact]
    public async Task User_Without_Import_Permissions_Should_Be_Forbidden()
    {
        var ctx = await SeedAsync();
        var batchId = await SeedBatchAsync(ctx.TenantId, null, _ => { });
        var client = ClientFor(await TokenAsync(ctx, new[] { "Clients.Read" }));

        (await client.GetAsync("/api/v1/imports/batches")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/v1/imports/batches/{batchId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await PostAsync(client, Guid.NewGuid(), "x", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.DeleteAsync($"/api/v1/imports/batches/{batchId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var readOnly = ClientFor(await TokenAsync(ctx, new[] { "Imports.Read" }));
        (await readOnly.GetAsync($"/api/v1/imports/batches/{batchId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await readOnly.DeleteAsync($"/api/v1/imports/batches/{batchId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Database_Should_Allow_Only_One_Active_Job_Per_Tenant()
    {
        var ctx = await SeedAsync();
        await SeedBatchAsync(ctx.TenantId, null, ToValidating);

        var second = () => SeedBatchAsync(ctx.TenantId, null, ToValidating);

        await second.Should().ThrowAsync<DbUpdateException>();
        // Başka kiracıda aktif iş engellenmez.
        var other = await SeedAsync();
        await SeedBatchAsync(other.TenantId, null, ToValidating);
    }

    [Fact]
    public async Task Database_Should_Reject_Same_Content_Hash_Until_Batch_Is_Discarded()
    {
        var ctx = await SeedAsync();
        var hash = new string('a', ImportBatch.ContentHashLength);
        var firstId = await SeedBatchAsync(ctx.TenantId, null, _ => { }, hash);

        var duplicate = () => SeedBatchAsync(ctx.TenantId, null, _ => { }, hash);
        await duplicate.Should().ThrowAsync<DbUpdateException>();

        (await ClientFor(ctx.Token).DeleteAsync($"/api/v1/imports/batches/{firstId}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        await SeedBatchAsync(ctx.TenantId, null, _ => { }, hash);
    }

    private static void ToValidating(ImportBatch b)
    {
        b.ApplyMapping("{}", null, DateTime.UtcNow);
        b.StartValidation(DateTime.UtcNow);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, Guid requestId, string? name, Guid? clinicId)
    {
        var form = new MultipartFormDataContent { { new StringContent(requestId.ToString()), "requestId" } };
        // B2: dosya zorunlu; içerik requestId'den türetilir (aynı requestId = aynı dosya, farklı requestId = farklı özet).
        var csv = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes($"AdSoyad;Telefon\nTest {requestId};05321234567\n"));
        csv.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(csv, "customersFile", "musteriler.csv");
        if (name is not null)
            form.Add(new StringContent(name), "name");
        if (clinicId is { } c)
            form.Add(new StringContent(c.ToString()), "clinicId");
        return await client.PostAsync("/api/v1/imports/batches", form);
    }

    private static async Task<Guid> IdAsync(HttpResponseMessage response)
    {
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
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

    private async Task<int> BatchCountAsync(Guid tenantId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ImportBatches.AsNoTracking().CountAsync(b => b.TenantId == tenantId);
    }

    private async Task<ImportBatchStatus> StatusAsync(Guid batchId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ImportBatches.AsNoTracking().Where(b => b.Id == batchId).Select(b => b.Status).SingleAsync();
    }

    /// <summary>Partiyi doğrudan veritabanına yazar; <paramref name="contentHash"/> B2 yüklemesini taklit eder.</summary>
    private async Task<Guid> SeedBatchAsync(Guid tenantId, Guid? clinicId, Action<ImportBatch> drive, string? contentHash = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var batch = ImportBatch.Create(tenantId, clinicId, Guid.NewGuid(), "Tohum", Guid.NewGuid(), DateTime.UtcNow).Value!;
        drive(batch);
        db.ImportBatches.Add(batch);
        if (contentHash is not null)
            db.Entry(batch).Property(b => b.ContentHash).CurrentValue = contentHash;
        await db.SaveChangesAsync();
        return batch.Id;
    }

    private async Task<Guid> SeedClinicAsync(Guid tenantId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clinic = new Clinic(tenantId, $"Imp-{Guid.NewGuid():N}"[..14], "Ankara");
        db.Clinics.Add(clinic);
        await db.SaveChangesAsync();
        return clinic.Id;
    }

    private async Task<string> TokenAsync(SeedCtx ctx, string[] permissions)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        return await IntegrationTestAuthHelper.SeedScopedClinicUserAndIssueTokenAsync(
            db, jwt, hasher, ctx.TenantId, ctx.ClinicId, permissions);
    }

    private async Task<SeedCtx> SeedAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var tenant = new Tenant($"imp-{Guid.NewGuid():N}"[..18]);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        var clinic = new Clinic(tenant.Id, $"Imp-{Guid.NewGuid():N}"[..14], "Istanbul");
        db.Clinics.Add(clinic);
        await db.SaveChangesAsync();

        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);
        var token = await IntegrationTestAuthHelper.SeedScopedClinicUserAndIssueTokenAsync(
            db, jwt, hasher, tenant.Id, clinic.Id, ImportPermissions);

        return new SeedCtx(tenant.Id, clinic.Id, token);
    }

    private sealed record SeedCtx(Guid TenantId, Guid ClinicId, string Token);
}
