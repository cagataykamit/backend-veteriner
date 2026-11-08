using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.DataExports;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Notifications;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Infrastructure.DataExports;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backend.IntegrationTests.DataExports;

/// <summary>
/// Tüm veri dışa aktarma (DATA_EXPORT_API_CONTRACT.md; gerçek SQL Server/LocalDB): ZIP içeriği ve dosya listesi, hassas alanların
/// yokluğu, kiracı izolasyonu, salt okunur abonelik, yetki, tek açık iş ve talep sınırı, süre dolumu, denetim kaydı ve bildirim.
/// Sentetik veri. Arka plan işçisi test yapılandırmasında kapalıdır; işçi testte doğrudan çalıştırılır.
/// </summary>
[Collection("data-exports-api")]
public sealed class DataExportIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Password = "Veri-Aktar-Test-1";
    private const string ValidNationalId = "10000000146";

    private readonly CustomWebApplicationFactory _factory;

    public DataExportIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Export_Should_Package_All_Tenant_Data_As_Csv_Zip_Without_Secrets_Or_Other_Tenants()
    {
        var ctx = await SeedTenantAsync();
        var data = await SeedClinicalDataAsync(ctx);
        var foreign = await SeedTenantAsync();
        var foreignData = await SeedClinicalDataAsync(foreign, clientName: $"YABANCI-{Guid.NewGuid():N}"[..20]);
        var client = ClientFor(ctx.Token);

        var requested = await client.PostAsync("/api/v1/data-exports", null);
        requested.StatusCode.Should().Be(HttpStatusCode.Accepted, await requested.Content.ReadAsStringAsync());
        var job = await requested.Content.ReadFromJsonAsync<JsonElement>();
        job.GetProperty("status").GetString().Should().Be("Queued");
        var jobId = job.GetProperty("id").GetGuid();

        await RunWorkerAsync();

        var overview = await GetJsonAsync(client, "/api/v1/data-exports");
        var row = overview.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == jobId);
        row.GetProperty("status").GetString().Should().Be("Ready");
        row.GetProperty("canDownload").GetBoolean().Should().BeTrue();
        row.TryGetProperty("storageKey", out _).Should().BeFalse();
        overview.GetProperty("retentionDays").GetInt32().Should().Be(7);
        overview.GetProperty("remainingRequests").GetInt32().Should().Be(DataExportJob.MaxRequestsPerWindow - 1);
        overview.GetProperty("hasOpenJob").GetBoolean().Should().BeFalse();

        var download = await client.GetAsync($"/api/v1/data-exports/{jobId}/download");
        download.StatusCode.Should().Be(HttpStatusCode.OK, await download.Content.ReadAsStringAsync());
        download.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        download.Headers.CacheControl!.NoStore.Should().BeTrue();
        download.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");

        var files = await ReadZipAsync(await download.Content.ReadAsByteArrayAsync());

        files.Keys.Should().BeEquivalentTo(DataExportTables.All.Select(t => t.FileName).Append("BENIOKU.txt"));
        foreach (var (name, (bytes, _)) in files)
            bytes.Take(3).Should().Equal([0xEF, 0xBB, 0xBF], $"{name} UTF-8 BOM ile başlar");
        foreach (var table in DataExportTables.All)
            Text(files, table.FileName).Split("\r\n")[0].Split(';')[0].Should().Be("Id", $"{table.FileName} kayıt kimliğiyle başlar");

        var clients = Text(files, "musteriler.csv");
        clients.Should().Contain(data.ClientId.ToString("D")).And.Contain(ValidNationalId).And.Contain(data.ClientName);
        Text(files, "klinikler.csv").Should().Contain(ctx.ClinicId.ToString("D"));
        Text(files, "hastalar.csv").Should().Contain("Allergy, Aggressive").And.Contain("Penisilin alerjisi").And.Contain(data.ClientId.ToString("D"));
        var exams = Text(files, "muayeneler.csv");
        exams.Should().Contain("Bulgu metni").And.Contain("Finalized").And.Contain(ctx.UserId.ToString("D"));
        Text(files, "muayene_ek_notlari.csv").Should().Contain("Ek not metni ve düzeltme").And.Contain(data.ExaminationId.ToString("D"));
        Text(files, "receteler.csv").Should().Contain(data.PrescriptionId.ToString("D"));
        Text(files, "recete_satirlari.csv").Should().Contain("Amoksisilin 250 mg").And.Contain("Günde 2 kez");
        Text(files, "odemeler.csv").Should().Contain(";150.00;TRY;Cash;");
        Text(files, "kullanicilar.csv").Should().Contain(ctx.UserId.ToString("D"));

        // Zaman iki sütun: UTC ve İstanbul.
        var examHeader = exams.Split("\r\n")[0];
        examHeader.Should().Contain("MuayeneUtc;MuayeneIstanbul");
        exams.Should().MatchRegex(@"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z;\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\+03:00");

        var readme = Text(files, "BENIOKU.txt");
        readme.Should().Contain($"Biçim sürümü: {Backend.Veteriner.Application.DataExports.DataExportFormat.Version}");
        readme.Should().Contain("- musteriler.csv: Müşteriler (hasta sahipleri) (1 kayıt)");
        readme.Should().Contain("Pakette olmayanlar");

        // Hassas alanlar ve başka kiracı yok.
        string storageKey;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == ctx.UserId);
            storageKey = (await db.DataExportJobs.AsNoTracking().SingleAsync(j => j.Id == jobId)).StorageKey!;
            var all = string.Join("\n", files.Values.Select(v => v.Text));
            all.Should().NotContain(user.PasswordHash);
            all.Should().NotContain(ctx.Email);
            all.Should().NotContain(storageKey);
            all.Should().NotContain("PasswordHash").And.NotContain("RowVersion").And.NotContain("RequestHash").And.NotContain("LeaseOwner");
            all.Should().NotContain(foreignData.ClientName);
            all.Should().NotContain(foreign.ClinicId.ToString("D"));
            all.Should().NotContain(foreignData.ClientId.ToString("D"));

            // Bildirim yalnız talep edene; denetim kaydı talep ve indirme için.
            var notifications = await db.UserNotifications.AsNoTracking().Where(n => n.TargetId == jobId).ToListAsync();
            notifications.Should().ContainSingle();
            notifications[0].UserId.Should().Be(ctx.UserId);
            notifications[0].Type.Should().Be(UserNotificationType.DataExportReady);
            notifications[0].TargetType.Should().Be("DataExport");

            (await db.AuditLogs.AsNoTracking().AnyAsync(a => a.Action == "DataExport.Request" && a.ActorUserId == ctx.UserId && a.Success))
                .Should().BeTrue();
            (await db.AuditLogs.AsNoTracking().AnyAsync(a =>
                    a.Action == "DataExport.Download" && a.TargetId == $"DataExportJobId={jobId}" && a.ActorUserId == ctx.UserId && a.Success))
                .Should().BeTrue();
        }

        // Başka kiracının kullanıcısı bu işi göremez ve indiremez.
        var foreignClient = ClientFor(foreign.Token);
        (await foreignClient.GetAsync($"/api/v1/data-exports/{jobId}/download")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await GetJsonAsync(foreignClient, "/api/v1/data-exports")).GetProperty("items").EnumerateArray()
            .Should().NotContain(i => i.GetProperty("id").GetGuid() == jobId);
    }

    [Fact]
    public async Task Request_Should_Allow_Single_Open_Job_And_Limit_Requests_Per_Window()
    {
        var ctx = await SeedTenantAsync();
        var client = ClientFor(ctx.Token);

        (await client.PostAsync("/api/v1/data-exports", null)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var second = await client.PostAsync("/api/v1/data-exports", null);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(second)).Should().Be("DataExports.AnotherJobRunning");
        (await GetJsonAsync(client, "/api/v1/data-exports")).GetProperty("hasOpenJob").GetBoolean().Should().BeTrue();

        await RunWorkerAsync();
        (await client.PostAsync("/api/v1/data-exports", null)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await RunWorkerAsync();
        (await client.PostAsync("/api/v1/data-exports", null)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await RunWorkerAsync();

        var overLimit = await client.PostAsync("/api/v1/data-exports", null);
        overLimit.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(overLimit)).Should().Be("DataExports.LimitReached");
        (await GetJsonAsync(client, "/api/v1/data-exports")).GetProperty("remainingRequests").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Export_Should_Work_For_Read_Only_Subscription()
    {
        var ctx = await SeedTenantAsync(readOnly: true);
        await SeedClinicalDataAsync(ctx);
        var client = ClientFor(ctx.Token);

        var requested = await client.PostAsync("/api/v1/data-exports", null);
        requested.StatusCode.Should().Be(HttpStatusCode.Accepted, await requested.Content.ReadAsStringAsync());
        var jobId = (await requested.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await RunWorkerAsync();

        var download = await client.GetAsync($"/api/v1/data-exports/{jobId}/download");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadZipAsync(await download.Content.ReadAsByteArrayAsync())).Should().ContainKey("musteriler.csv");
    }

    [Fact]
    public async Task Endpoints_Should_Return_403_Without_Data_Export_Permission()
    {
        var ctx = await SeedTenantAsync(permissions: [PermissionCatalog.Clients.Read, PermissionCatalog.Imports.Commit]);
        var client = ClientFor(ctx.Token);

        (await client.GetAsync("/api/v1/data-exports")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsync("/api/v1/data-exports", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/v1/data-exports/{Guid.NewGuid()}/download")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Maintenance_Should_Delete_Expired_Package_And_Download_Should_Return_410()
    {
        var ctx = await SeedTenantAsync();
        var client = ClientFor(ctx.Token);
        var requested = await client.PostAsync("/api/v1/data-exports", null);
        var jobId = (await requested.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await RunWorkerAsync();

        var storage = _factory.Services.GetRequiredService<IFileStorage>();
        string key;
        DateTime expiresAt;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var job = await scope.ServiceProvider.GetRequiredService<AppDbContext>().DataExportJobs.AsNoTracking().SingleAsync(j => j.Id == jobId);
            job.Status.Should().Be(DataExportJobStatus.Ready);
            key = job.StorageKey!;
            expiresAt = job.ExpiresAtUtc!.Value;
        }

        await using (var stream = await storage.OpenReadAsync(key, CancellationToken.None))
            stream.Should().NotBeNull();

        var early = Processor(new FakeTimeProvider(expiresAt.AddMinutes(-1)));
        await early.RunMaintenanceAsync(CancellationToken.None);
        (await client.GetAsync($"/api/v1/data-exports/{jobId}/download")).StatusCode.Should().Be(HttpStatusCode.OK);

        var late = Processor(new FakeTimeProvider(expiresAt.AddMinutes(1)));
        (await late.RunMaintenanceAsync(CancellationToken.None)).Should().BeGreaterThanOrEqualTo(1);

        (await storage.OpenReadAsync(key, CancellationToken.None)).Should().BeNull();
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var job = await scope.ServiceProvider.GetRequiredService<AppDbContext>().DataExportJobs.AsNoTracking().SingleAsync(j => j.Id == jobId);
            job.Status.Should().Be(DataExportJobStatus.Expired);
            job.StorageKey.Should().BeNull();
        }

        var gone = await client.GetAsync($"/api/v1/data-exports/{jobId}/download");
        gone.StatusCode.Should().Be(HttpStatusCode.Gone);
        (await IntegrationTestProblemDetails.ReadCodeAsync(gone)).Should().Be("DataExports.Expired");
    }

    [Fact]
    public void Contract_Should_List_Every_Package_File()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docs", "DATA_EXPORT_API_CONTRACT.md")))
            dir = dir.Parent;

        dir.Should().NotBeNull("sözleşme deposun docs klasöründedir");
        var contract = File.ReadAllText(Path.Combine(dir!.FullName, "docs", "DATA_EXPORT_API_CONTRACT.md"));
        foreach (var table in DataExportTables.All)
            contract.Should().Contain($"| `{table.FileName}` |", "dosya listesi sözleşmeyle aynı kalmalı");
    }

    // ================================================================ Yardımcılar

    private sealed record TenantCtx(Guid TenantId, Guid ClinicId, Guid UserId, string Email, string Token);

    private sealed record ClinicalData(Guid ClientId, string ClientName, Guid ExaminationId, Guid PrescriptionId);

    private DataExportProcessor Processor(TimeProvider time)
        => new(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            _factory.Services.GetRequiredService<IFileStorage>(),
            time,
            NullLogger<DataExportProcessor>.Instance,
            $"export:test:{Guid.NewGuid():N}"[..40]);

    /// <summary>Kuyruktaki tüm işleri bitirir (başka testlerin açık işleri dahil; sıra talep anına göre).</summary>
    private async Task RunWorkerAsync()
    {
        var processor = _factory.Services.GetRequiredService<DataExportProcessor>();
        for (var i = 0; i < 20 && await processor.ProcessNextAsync(CancellationToken.None); i++)
        {
        }
    }

    private async Task<TenantCtx> SeedTenantAsync(bool readOnly = false, IReadOnlyCollection<string>? permissions = null)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var now = DateTime.UtcNow;

        var tenant = new Tenant($"DX-{Guid.NewGuid():N}"[..16], now);
        db.Tenants.Add(tenant);
        if (readOnly)
            db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, now.AddDays(-40), trialDays: 7));
        var clinic = new Clinic(tenant.Id, $"DX Klinik {Guid.NewGuid():N}"[..20], "İzmir");
        db.Clinics.Add(clinic);
        var email = $"dx-{Guid.NewGuid():N}@example.com";
        var user = new User(email, hasher.Hash(Password), now);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.UserTenants.Add(new UserTenant(user.Id, tenant.Id, now));
        await db.SaveChangesAsync();

        var claims = (permissions ?? [PermissionCatalog.DataExport.Manage])
            .Select(p => new Claim("permission", p))
            .Append(new Claim(VeterinerClaims.TenantId, tenant.Id.ToString("D")))
            .ToList();
        var (token, _, _) = jwt.Create(user.Id, email, Array.Empty<string>(), claims);
        return new TenantCtx(tenant.Id, clinic.Id, user.Id, email, token);
    }

    private async Task<ClinicalData> SeedClinicalDataAsync(TenantCtx ctx, string? clientName = null)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var name = clientName ?? $"DX Müşteri {Guid.NewGuid():N}"[..20];

        var client = new Client(ctx.TenantId, name, now, "905551110088", $"musteri-{Guid.NewGuid():N}@example.com");
        client.ApplyInvoicingIdentity(ClientPersonType.Individual, ValidNationalId, null, null, null, null, null, null, now).IsSuccess.Should().BeTrue();
        db.Clients.Add(client);
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(ctx.TenantId, client.Id, "Pamuk", speciesId);
        pet.ApplyAlerts(["Allergy", "Aggressive"], "Penisilin alerjisi").IsSuccess.Should().BeTrue();
        db.Pets.Add(pet);
        await db.SaveChangesAsync();

        var exam = new Examination(ctx.TenantId, ctx.ClinicId, pet.Id, appointmentId: null, now.AddHours(-2), "Kontrol", "Bulgu metni", null, null, utcNow: now);
        db.Examinations.Add(exam);
        await db.SaveChangesAsync();
        exam.Finalize(ctx.UserId, now.AddHours(-1)).IsSuccess.Should().BeTrue();
        var addendum = exam.AddAddendum(ctx.UserId, "Düzeltme", "Ek not metni ve düzeltme", now);
        addendum.IsSuccess.Should().BeTrue();
        db.ExaminationAddenda.Add(addendum.Value!);

        var prescription = new Prescription(ctx.TenantId, ctx.ClinicId, pet.Id, exam.Id, null, now.AddHours(-1), "Reçete", "İçerik", null, null, now);
        prescription.ReplaceItems([new PrescriptionItemDraft("Amoksisilin 250 mg", null, "Günde 2 kez", 10m)]).IsSuccess.Should().BeTrue();
        db.Prescriptions.Add(prescription);
        db.Payments.Add(new Payment(ctx.TenantId, ctx.ClinicId, client.Id, pet.Id, null, null, 150m, "TRY", PaymentMethod.Cash, now.AddMinutes(-30), "Nakit"));
        await db.SaveChangesAsync();

        return new ClinicalData(client.Id, name, exam.Id, prescription.Id);
    }

    private HttpClient ClientFor(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<Dictionary<string, (byte[] Bytes, string Text)>> ReadZipAsync(byte[] zipBytes)
    {
        var result = new Dictionary<string, (byte[], string)>();
        await using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            await using var stream = await entry.OpenAsync();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            var bytes = buffer.ToArray();
            result[entry.FullName] = (bytes, new UTF8Encoding(false).GetString(bytes).TrimStart('﻿'));
        }

        return result;
    }

    private static string Text(Dictionary<string, (byte[] Bytes, string Text)> files, string name) => files[name].Text;
}
