using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Files;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.IntegrationTests.Files;

/// <summary>
/// Yükleme virüs taraması uçtan uca (IMAGING_API_CONTRACT.md §5), sahte tarayıcıyla: virüslü dosya 422
/// <c>Files.MalwareDetected</c> ve imzalı denetim kaydı, tarayıcıya ulaşılamazsa 503 <c>Files.ScanUnavailable</c>; her iki durumda
/// hiçbir şey kaydedilmez. İçe aktarma dosyaları da aynı kuraldan geçer. Tarama açıkken <c>/health/ready</c> clamd'yi yoklar.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class FileScanningIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, .. "JFIF"u8.ToArray(), 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01];

    private readonly CustomWebApplicationFactory _factory;

    public FileScanningIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private WebApplicationFactory<Program> WithScanner(FileScanResult result)
        => _factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IFileMalwareScanner>();
            services.AddSingleton<IFileMalwareScanner>(new FixedScanner(result));
        }));

    private async Task<HttpClient> AdminClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (email, password, _) = await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);
        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    private async Task<(HttpClient Client, Guid RecordId)> ImagingRecordAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (email, password, clinicId) = await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, clinicId);
        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.PostAsJsonAsync("/api/v1/imaging-records", new
        {
            ClinicId = clinicId,
            PetId = petId,
            PerformedAtUtc = DateTime.UtcNow.AddHours(-1),
            Modality = "XRay",
            Description = "Toraks",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (client, await response.Content.ReadFromJsonAsync<Guid>());
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid recordId)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Jpeg);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", "rontgen.jpg");
        return client.PostAsync($"/api/v1/imaging-records/{recordId}/files", form);
    }

    private async Task<int> StoredFileCountAsync(Guid ownerId)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.StoredFiles.IgnoreQueryFilters().AsNoTracking().CountAsync(f => f.OwnerId == ownerId);
    }

    [Fact]
    public async Task Infected_Upload_Should_Return_422_Write_Nothing_And_Audit_Signature()
    {
        using var factory = WithScanner(FileScanResult.Infected("Win.Test.EICAR_HDB-1"));
        var (client, recordId) = await ImagingRecordAsync(factory);

        var response = await UploadAsync(client, recordId);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("Files.MalwareDetected");
        body.GetProperty("detail").GetString().Should().Be("Dosyada zararlı içerik bulundu; yüklenmedi.");
        (await StoredFileCountAsync(recordId)).Should().Be(0);

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == UploadMalwareGuard.AuditAction && a.TargetId == $"ImagingRecordId={recordId}")
            .SingleAsync();
        audit.Success.Should().BeFalse();
        audit.FailureReason.Should().Be("Signature=Win.Test.EICAR_HDB-1");
    }

    [Fact]
    public async Task Unavailable_Scanner_Should_Return_503_And_Write_Nothing()
    {
        using var factory = WithScanner(FileScanResult.Failed("clamd unreachable"));
        var (client, recordId) = await ImagingRecordAsync(factory);

        var response = await UploadAsync(client, recordId);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("Files.ScanUnavailable");
        body.GetProperty("detail").GetString().Should().Be("Dosya şu an taranamıyor; biraz sonra tekrar deneyin.");
        (await StoredFileCountAsync(recordId)).Should().Be(0);
    }

    [Fact]
    public async Task Infected_Import_File_Should_Be_Rejected_Without_Batch()
    {
        using var factory = WithScanner(FileScanResult.Infected("Xls.Trojan.Agent-1"));
        var client = await AdminClientAsync(factory);
        var requestId = Guid.NewGuid();

        var form = new MultipartFormDataContent { { new StringContent(requestId.ToString()), "requestId" } };
        var csv = new ByteArrayContent(Encoding.UTF8.GetBytes("AdSoyad;Telefon\nTest Kişi;05321234567\n"));
        csv.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(csv, "customersFile", "musteriler.csv");
        var response = await client.PostAsync("/api/v1/imports/batches", form);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await response.Content.ReadAsStringAsync());
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Files.MalwareDetected");

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.ImportBatches.IgnoreQueryFilters().AsNoTracking().AnyAsync(b => b.RequestId == requestId)).Should().BeFalse();
        (await db.AuditLogs.AsNoTracking().AnyAsync(a =>
            a.Action == UploadMalwareGuard.AuditAction && a.TargetId == $"RequestId={requestId}")).Should().BeTrue();
    }

    private WebApplicationFactory<Program> WithUnreachableClamd()
        => _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileScanning:Enabled"] = "true",
                ["FileScanning:Clamd:Port"] = ClosedPort().ToString(),
                ["FileScanning:Clamd:TimeoutSeconds"] = "2",
            })));

    [Fact]
    public async Task Enabled_Scanning_With_Unreachable_Clamd_Should_Reject_Upload()
    {
        using var factory = WithUnreachableClamd();
        var (client, recordId) = await ImagingRecordAsync(factory);

        var response = await UploadAsync(client, recordId);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Files.ScanUnavailable");
        (await StoredFileCountAsync(recordId)).Should().Be(0);
    }

    [Fact]
    public async Task Health_Ready_Should_Report_Unreachable_Clamd_As_Degraded_When_Enabled()
    {
        using var factory = WithUnreachableClamd();

        var response = await factory.CreateClient().GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var entry = document.RootElement.GetProperty("results").GetProperty("clamav");
        entry.GetProperty("status").GetString().Should().Be("Degraded");
        entry.GetProperty("description").GetString().Should().Contain("clamd yanıt vermiyor");
    }

    [Fact]
    public async Task Health_Ready_Should_Say_Scanning_Is_Off_When_Disabled()
    {
        var response = await _factory.CreateClient().GetAsync("/health/ready");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var entry = document.RootElement.GetProperty("results").GetProperty("clamav");
        entry.GetProperty("status").GetString().Should().Be("Healthy");
        entry.GetProperty("description").GetString().Should().Contain("kapalı");
    }

    private static int ClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class FixedScanner(FileScanResult result) : IFileMalwareScanner
    {
        public async Task<FileScanResult> ScanAsync(Stream content, CancellationToken ct)
        {
            await content.CopyToAsync(Stream.Null, ct);
            return result;
        }
    }
}
