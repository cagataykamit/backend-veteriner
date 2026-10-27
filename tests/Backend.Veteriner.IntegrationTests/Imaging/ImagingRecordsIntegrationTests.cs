using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Files;
using Backend.Veteriner.Domain.Imaging;
using Backend.Veteriner.Infrastructure.Files;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Imaging;

/// <summary>
/// Görüntüleme kaydı ve dosya katmanı uçtan uca (docs/IMAGING_API_CONTRACT.md): yükleme ve içerikten tür (sahte uzantı reddi),
/// boyut sınırı, yanıt başlıkları, kiracı/klinik IDOR, imzalı URL ve süresi, iptal sonrası erişimin kapanması, kesinleşme
/// kilidi, izin matrisi, lab dosyası ve yarım yükleme temizliği.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class ImagingRecordsIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, .. "JFIF"u8.ToArray(), 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01];
    private static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj << >> endobj\ntrailer << >>\n%%EOF"u8.ToArray();

    private readonly CustomWebApplicationFactory _factory;

    public ImagingRecordsIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private async Task<(HttpClient Client, Guid ClinicId, Guid PetId)> AdminClientAsync(WebApplicationFactory<Program>? factory = null)
    {
        var f = factory ?? _factory;
        var client = f.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (email, password, clinicId) = await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, clinicId);
        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return (client, clinicId, petId);
    }

    private static async Task<Guid> CreateRecordAsync(HttpClient client, Guid clinicId, Guid petId, Guid? examinationId = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/imaging-records", new
        {
            ClinicId = clinicId,
            PetId = petId,
            ExaminationId = examinationId,
            PerformedAtUtc = DateTime.UtcNow.AddHours(-1),
            Modality = "XRay",
            Description = "Toraks LL",
            ClinicalNote = "Kardiyomegali yok",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, string ownerPath, byte[] content, string fileName, string declaredType = "image/jpeg")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(declaredType);
        form.Add(file, "file", fileName);
        return client.PostAsync($"{ownerPath}/files", form);
    }

    private static async Task<Guid> UploadOkAsync(HttpClient client, string ownerPath, byte[] content, string fileName)
    {
        var response = await UploadAsync(client, ownerPath, content, fileName);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, Guid id)
        => await client.GetFromJsonAsync<JsonElement>($"/api/v1/imaging-records/{id}");

    [Fact]
    public async Task Upload_Should_Detect_Type_From_Content_And_Stream_With_Safe_Headers()
    {
        var (client, clinicId, petId) = await AdminClientAsync();
        var id = await CreateRecordAsync(client, clinicId, petId);

        // Uzantı ve bildirilen tür yanlış; içerik JPEG.
        var fileId = await UploadOkAsync(client, $"/api/v1/imaging-records/{id}", Jpeg, "../../röntgen.exe");

        var detail = await GetAsync(client, id);
        detail.GetProperty("modality").GetString().Should().Be("XRay");
        var file = detail.GetProperty("files").EnumerateArray().Should().ContainSingle().Subject;
        file.GetProperty("id").GetGuid().Should().Be(fileId);
        file.GetProperty("contentType").GetString().Should().Be("image/jpeg");
        file.GetProperty("fileName").GetString().Should().Be("röntgen.exe");
        file.GetProperty("sizeBytes").GetInt64().Should().Be(Jpeg.Length);
        file.TryGetProperty("storageKey", out _).Should().BeFalse();

        var inline = await client.GetAsync($"/api/v1/imaging-records/{id}/files/{fileId}/content");
        inline.StatusCode.Should().Be(HttpStatusCode.OK);
        inline.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
        inline.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        inline.Content.Headers.ContentDisposition!.DispositionType.Should().Be("inline");
        (await inline.Content.ReadAsByteArrayAsync()).Should().Equal(Jpeg);

        var download = await client.GetAsync($"/api/v1/imaging-records/{id}/files/{fileId}/content?download=true");
        download.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        download.Content.Headers.ContentDisposition.FileNameStar.Should().Be("röntgen.jpg");

        var list = await client.GetFromJsonAsync<JsonElement>($"/api/v1/imaging-records?clinicId={clinicId}&petId={petId}");
        var item = list.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == id);
        item.GetProperty("fileCount").GetInt32().Should().Be(1);
    }

    [Theory]
    [InlineData("<html><script>alert(1)</script></html>", "röntgen.jpg", "image/jpeg")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"/>", "görüntü.png", "image/svg+xml")]
    [InlineData("MZ\u0090\u0000 not an image", "sonuc.pdf", "application/pdf")]
    public async Task Fake_Extension_Should_Be_Rejected_With_415(string content, string fileName, string declaredType)
    {
        var (client, clinicId, petId) = await AdminClientAsync();
        var id = await CreateRecordAsync(client, clinicId, petId);

        var response = await UploadAsync(client, $"/api/v1/imaging-records/{id}", Encoding.UTF8.GetBytes(content), fileName, declaredType);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Files.UnsupportedType");
        (await GetAsync(client, id)).GetProperty("files").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task File_Over_Configured_Limit_Should_Return_413_And_Keep_Record()
    {
        using var small = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["FileStorage:MaxFileBytes"] = "1024" })));
        var (client, clinicId, petId) = await AdminClientAsync(small);
        var id = await CreateRecordAsync(client, clinicId, petId);

        var big = Jpeg.Concat(new byte[2048]).ToArray();
        var response = await UploadAsync(client, $"/api/v1/imaging-records/{id}", big, "buyuk.jpg");

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Files.TooLarge");
        (await GetAsync(client, id)).GetProperty("description").GetString().Should().Be("Toraks LL");
        (await UploadAsync(client, $"/api/v1/imaging-records/{id}", Jpeg, "kucuk.jpg")).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Signed_Url_Should_Work_Without_Bearer_And_Reject_Tampered_And_Expired()
    {
        var (client, clinicId, petId) = await AdminClientAsync();
        var id = await CreateRecordAsync(client, clinicId, petId);
        var fileId = await UploadOkAsync(client, $"/api/v1/imaging-records/{id}", Jpeg, "a.jpg");

        var issued = await client.PostAsync($"/api/v1/imaging-records/{id}/files/{fileId}/access-url", null);
        issued.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await issued.Content.ReadFromJsonAsync<JsonElement>();
        var url = body.GetProperty("url").GetString()!;
        body.GetProperty("expiresAtUtc").GetDateTime().Should().BeCloseTo(DateTime.UtcNow.AddMinutes(5), TimeSpan.FromSeconds(30));

        var anonymous = _factory.CreateClient();
        var ok = await anonymous.GetAsync(url);
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        ok.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
        (await ok.Content.ReadAsByteArrayAsync()).Should().Equal(Jpeg);

        var tampered = await anonymous.GetAsync(url[..^4] + "AAAA");
        tampered.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(tampered)).Should().Be("Files.AccessUrlInvalid");

        var signer = _factory.Services.GetRequiredService<IFileAccessSigner>();
        var past = DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeSeconds();
        var expired = await anonymous.GetAsync($"/api/v1/files/signed/{fileId}?expires={past}&signature={signer.Sign(fileId, past)}");
        expired.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(expired)).Should().Be("Files.AccessUrlInvalid");

        // İmza başka dosyaya taşınamaz.
        var otherFileId = await UploadOkAsync(client, $"/api/v1/imaging-records/{id}", Jpeg, "b.jpg");
        var moved = await anonymous.GetAsync(url.Replace(fileId.ToString("D"), otherFileId.ToString("D")));
        moved.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Bearer akış ucu anonim çağrılamaz.
        (await anonymous.GetAsync($"/api/v1/imaging-records/{id}/files/{fileId}/content")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Cancel_Should_Close_File_Access_And_Hide_From_Default_List()
    {
        var (client, clinicId, petId) = await AdminClientAsync();
        var id = await CreateRecordAsync(client, clinicId, petId);
        var fileId = await UploadOkAsync(client, $"/api/v1/imaging-records/{id}", Jpeg, "a.jpg");
        var url = (await (await client.PostAsync($"/api/v1/imaging-records/{id}/files/{fileId}/access-url", null))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString()!;
        var version = (await GetAsync(client, id)).GetProperty("rowVersion").GetString();

        var cancel = await client.PostAsJsonAsync($"/api/v1/imaging-records/{id}/cancel", new { Reason = "Yanlış hastaya yüklendi", RowVersion = version });
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await GetAsync(client, id);
        detail.GetProperty("status").GetString().Should().Be("Cancelled");
        detail.GetProperty("files").GetArrayLength().Should().Be(0);
        (await client.GetAsync($"/api/v1/imaging-records/{id}/files/{fileId}/content")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _factory.CreateClient().GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await UploadAsync(client, $"/api/v1/imaging-records/{id}", Jpeg, "c.jpg")).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var list = await client.GetFromJsonAsync<JsonElement>($"/api/v1/imaging-records?clinicId={clinicId}&petId={petId}");
        list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().NotContain(id);

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.StoredFiles.SingleAsync(f => f.Id == fileId)).Status.Should().Be(StoredFileStatus.Deleted);
    }

    [Fact]
    public async Task Timeline_Should_Show_Imaging_Title_Without_File_Data()
    {
        var (client, clinicId, petId) = await AdminClientAsync();
        var id = await CreateRecordAsync(client, clinicId, petId);
        await UploadOkAsync(client, $"/api/v1/imaging-records/{id}", Jpeg, "a.jpg");

        var timeline = await client.GetFromJsonAsync<JsonElement>($"/api/v1/pets/{petId}/timeline?clinicId={clinicId}");

        var item = timeline.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("sourceId").GetGuid() == id);
        item.GetProperty("type").GetString().Should().Be("Imaging");
        item.GetProperty("title").GetString().Should().Be("Röntgen");
        item.GetProperty("summary").GetString().Should().Be("Toraks LL");
        item.GetRawText().Should().NotContain("a.jpg");
    }

    [Fact]
    public async Task Stale_RowVersion_Should_Conflict_On_Update()
    {
        var (client, clinicId, petId) = await AdminClientAsync();
        var id = await CreateRecordAsync(client, clinicId, petId);
        var version = (await GetAsync(client, id)).GetProperty("rowVersion").GetString();
        object Body(string text) => new { PerformedAtUtc = DateTime.UtcNow.AddHours(-2), Modality = "Ultrasound", Description = text, RowVersion = version };

        (await client.PutAsJsonAsync($"/api/v1/imaging-records/{id}", Body("Sekme 1"))).StatusCode.Should().Be(HttpStatusCode.OK);
        var second = await client.PutAsJsonAsync($"/api/v1/imaging-records/{id}", Body("Sekme 2"));

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(second)).Should().Be("ImagingRecords.ConcurrencyConflict");
        (await GetAsync(client, id)).GetProperty("description").GetString().Should().Be("Sekme 1");
    }

    [Fact]
    public async Task Finalized_Examination_Should_Freeze_Existing_Record_But_Accept_New_Imaging()
    {
        var (client, clinicId, petId) = await AdminClientAsync();
        var examId = await SeedExaminationAsync(clinicId, petId);
        var existing = await CreateRecordAsync(client, clinicId, petId, examId);
        await FinalizeExaminationAsync(examId);

        var blocked = await UploadAsync(client, $"/api/v1/imaging-records/{existing}", Jpeg, "ek.jpg");
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(blocked)).Should().Be("Examinations.Finalized");

        var version = (await GetAsync(client, existing)).GetProperty("rowVersion").GetString();
        var edit = await client.PutAsJsonAsync($"/api/v1/imaging-records/{existing}", new
        {
            ExaminationId = examId, PerformedAtUtc = DateTime.UtcNow.AddHours(-1), Modality = "XRay", Description = "değişti", RowVersion = version,
        });
        edit.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var late = await CreateRecordAsync(client, clinicId, petId, examId);
        (await GetAsync(client, late)).GetProperty("addedAfterExaminationFinalization").GetBoolean().Should().BeTrue();
        await UploadOkAsync(client, $"/api/v1/imaging-records/{late}", Jpeg, "sonradan.jpg");

        var cancel = await client.PostAsJsonAsync($"/api/v1/imaging-records/{existing}/cancel", new { Reason = "Yanlış muayeneye bağlandı", RowVersion = version });
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Other_Tenant_And_Unassigned_Clinic_Should_Get_404_For_Record_File_And_Url()
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (email, _, assignedClinicId, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedLabResultReaderUserAsync(_factory.Services, hasher);
        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(
            _factory.Services, email, [PermissionCatalog.Imaging.Read, PermissionCatalog.Imaging.Create]);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var own = await SeedRecordWithFileAsync(assignedClinicId);
        var unassigned = await SeedRecordWithFileAsync(unassignedClinicId);
        var foreign = await SeedRecordWithFileAsync(await IntegrationTestAuthHelper.SeedClinicInForeignTenantAsync(_factory.Services));

        (await client.GetAsync($"/api/v1/imaging-records/{own.RecordId}/files/{own.FileId}/content")).StatusCode.Should().Be(HttpStatusCode.OK);

        foreach (var (recordId, fileId) in new[] { unassigned, foreign })
        {
            var detail = await client.GetAsync($"/api/v1/imaging-records/{recordId}");
            detail.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await IntegrationTestProblemDetails.ReadCodeAsync(detail)).Should().Be("ImagingRecords.NotFound");
            (await client.GetAsync($"/api/v1/imaging-records/{recordId}/files/{fileId}/content")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await client.PostAsync($"/api/v1/imaging-records/{recordId}/files/{fileId}/access-url", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await UploadAsync(client, $"/api/v1/imaging-records/{recordId}", Jpeg, "x.jpg")).StatusCode
                .Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);

            // Kendi kaydının yolu üzerinden başka kaydın dosyası istenemez.
            (await client.GetAsync($"/api/v1/imaging-records/{own.RecordId}/files/{fileId}/content")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task Read_Only_User_Should_Not_Create_Upload_Or_Cancel()
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (email, _, assignedClinicId, _) = await IntegrationTestAuthHelper.SeedLabResultReaderUserAsync(_factory.Services, hasher);
        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, [PermissionCatalog.Imaging.Read]);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var own = await SeedRecordWithFileAsync(assignedClinicId);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, assignedClinicId);

        (await client.GetAsync($"/api/v1/imaging-records/{own.RecordId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/v1/imaging-records", new { ClinicId = assignedClinicId, PetId = petId, PerformedAtUtc = DateTime.UtcNow, Modality = "XRay" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await UploadAsync(client, $"/api/v1/imaging-records/{own.RecordId}", Jpeg, "x.jpg")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync($"/api/v1/imaging-records/{own.RecordId}/cancel", new { Reason = "Deneme iptali", RowVersion = "AAAAAAAAAAA=" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var noImaging = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, [PermissionCatalog.LabResults.Read]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", noImaging);
        (await client.GetAsync($"/api/v1/imaging-records/{own.RecordId}/files/{own.FileId}/content")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Lab_Result_Should_Accept_Pdf_List_Stream_And_Close_On_Cancel()
    {
        var (client, clinicId, petId) = await AdminClientAsync();
        var create = await client.PostAsJsonAsync("/api/v1/lab-results", new { ClinicId = clinicId, PetId = petId, TestName = "Biyokimya" });
        var labId = await create.Content.ReadFromJsonAsync<Guid>();

        var fileId = await UploadOkAsync(client, $"/api/v1/lab-results/{labId}", Pdf, "sonuc.pdf");

        var files = await client.GetFromJsonAsync<JsonElement>($"/api/v1/lab-results/{labId}/files");
        var file = files.EnumerateArray().Should().ContainSingle().Subject;
        file.GetProperty("kind").GetString().Should().Be("Pdf");
        var content = await client.GetAsync($"/api/v1/lab-results/{labId}/files/{fileId}/content");
        content.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        // Lab detayı değişmedi (dosya listesi ayrı uçta).
        (await client.GetFromJsonAsync<JsonElement>($"/api/v1/lab-results/{labId}")).TryGetProperty("files", out _).Should().BeFalse();

        var version = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/lab-results/{labId}")).GetProperty("rowVersion").GetString();
        (await client.PostAsJsonAsync($"/api/v1/lab-results/{labId}/cancel", new { Reason = "Yanlış hasta için istendi", RowVersion = version }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.GetAsync($"/api/v1/lab-results/{labId}/files/{fileId}/content")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var afterCancel = await UploadAsync(client, $"/api/v1/lab-results/{labId}", Pdf, "ikinci.pdf");
        afterCancel.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(afterCancel)).Should().Be("LabResults.AlreadyCancelled");
    }

    [Fact]
    public async Task Maintenance_Should_Clean_Stale_Uploads_But_Keep_Ready_Files()
    {
        var clinicId = (await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(
            _factory.Services, _factory.Services.GetRequiredService<IPasswordHasher>())).ExtraClinicId;
        var ready = await SeedRecordWithFileAsync(clinicId);
        Guid staleId;
        string staleKey;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
            var tenantId = (await db.Clinics.SingleAsync(c => c.Id == clinicId)).TenantId;
            var stale = new StoredFile(tenantId, clinicId, StoredFileOwnerType.ImagingRecord, ready.RecordId, "yarim.jpg",
                StoredFileType.Jpeg, Guid.NewGuid(), DateTime.UtcNow.AddHours(-3));
            db.StoredFiles.Add(stale);
            await db.SaveChangesAsync();
            await storage.WriteAsync(stale.StorageKey, new MemoryStream(Jpeg), CancellationToken.None);
            (staleId, staleKey) = (stale.Id, stale.StorageKey);
        }

        var cleaned = await _factory.Services.GetRequiredService<StoredFileMaintenanceProcessor>().RunOnceAsync(CancellationToken.None);

        cleaned.Should().BeGreaterThanOrEqualTo(1);
        await using var check = _factory.Services.CreateSystemScope();
        var checkDb = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var checkStorage = check.ServiceProvider.GetRequiredService<IFileStorage>();
        (await checkDb.StoredFiles.SingleAsync(f => f.Id == staleId)).Status.Should().Be(StoredFileStatus.Deleted);
        (await checkStorage.OpenReadAsync(staleKey, CancellationToken.None)).Should().BeNull();
        (await checkDb.StoredFiles.SingleAsync(f => f.Id == ready.FileId)).Status.Should().Be(StoredFileStatus.Ready);
    }

    [Fact]
    public async Task Local_Storage_Should_Refuse_Foreign_Keys_And_Overwrite()
    {
        var storage = _factory.Services.GetRequiredService<IFileStorage>();
        var key = StoredFile.BuildStorageKey(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid());

        await storage.WriteAsync(key, new MemoryStream(Jpeg), CancellationToken.None);
        var overwrite = () => storage.WriteAsync(key, new MemoryStream(Pdf), CancellationToken.None);
        await overwrite.Should().ThrowAsync<IOException>();
        await using (var read = await storage.OpenReadAsync(key, CancellationToken.None))
        {
            using var buffer = new MemoryStream();
            await read!.CopyToAsync(buffer);
            buffer.ToArray().Should().Equal(Jpeg);
        }

        foreach (var bad in new[] { "../../appsettings.json", "..\\x", "/etc/passwd", "C:/Windows/win.ini", key + "/../x", key.ToUpperInvariant() })
        {
            var write = () => storage.WriteAsync(bad, new MemoryStream(Jpeg), CancellationToken.None);
            await write.Should().ThrowAsync<ArgumentException>($"'{bad}' kabul edilmemeli");
        }

        await storage.DeleteAsync(key, CancellationToken.None);
        (await storage.OpenReadAsync(key, CancellationToken.None)).Should().BeNull();
    }

    private async Task<(Guid RecordId, Guid FileId)> SeedRecordWithFileAsync(Guid clinicId)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var tenantId = (await db.Clinics.SingleAsync(c => c.Id == clinicId)).TenantId;
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, clinicId);
        var now = DateTime.UtcNow;

        var record = ImagingRecord.Create(tenantId, clinicId, petId, null, false, now.AddHours(-1), ImagingModality.Ultrasound,
            "IDOR testi", null, Guid.NewGuid(), now).Value!;
        db.ImagingRecords.Add(record);
        var file = new StoredFile(tenantId, clinicId, StoredFileOwnerType.ImagingRecord, record.Id, "a.jpg", StoredFileType.Jpeg, Guid.NewGuid(), now);
        await storage.WriteAsync(file.StorageKey, new MemoryStream(Jpeg), CancellationToken.None);
        file.MarkReady(Jpeg.Length, new string('a', 64), now);
        db.StoredFiles.Add(file);
        await db.SaveChangesAsync();
        return (record.Id, file.Id);
    }

    private async Task<Guid> SeedExaminationAsync(Guid clinicId, Guid petId)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenantId = (await db.Clinics.SingleAsync(c => c.Id == clinicId)).TenantId;
        var exam = new Examination(tenantId, clinicId, petId, appointmentId: null, DateTime.UtcNow.AddHours(-2), "Kontrol", "Bulgu", null, null, utcNow: DateTime.UtcNow);
        db.Examinations.Add(exam);
        await db.SaveChangesAsync();
        return exam.Id;
    }

    private async Task FinalizeExaminationAsync(Guid examId)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var exam = await db.Examinations.SingleAsync(e => e.Id == examId);
        exam.Finalize(Guid.NewGuid(), DateTime.UtcNow).IsSuccess.Should().BeTrue();
        await db.SaveChangesAsync();
    }
}
