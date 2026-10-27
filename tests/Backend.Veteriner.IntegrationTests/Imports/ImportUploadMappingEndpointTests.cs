using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using ClosedXML.Excel;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Imports;

/// <summary>
/// İçe aktarma B2 (gerçek SQL Server/LocalDB): dosya yükleme (XLSX/CSV), hazırlık satırları, mükerrer dosya, requestId
/// tekrarı, sütun/değer eşleme, taslak silmede satır temizliği, şablon indirme, yetki ve kiracı yalıtımı.
/// </summary>
[Collection("imports-api")]
public sealed class ImportUploadMappingEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] ImportPermissions = { "Imports.Read", "Imports.Manage" };
    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly CustomWebApplicationFactory _factory;

    public ImportUploadMappingEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    // ---------------------------------------------------------------- Yükleme

    [Fact]
    public async Task Xlsx_Upload_Should_Create_Batch_With_Rows_Columns_And_Hash()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var bytes = Workbook(
            ("Musteriler", [["Ad Soyad", "Cep", "E-posta"], ["Ayşe Yılmaz", "05321234567", "ayse@example.com"], ["Veli Kaya", null, null]]),
            ("Hayvanlar", [["Hayvan Adı", "Tür", "Sahip", "Sahip Tel"], ["Pamuk", "Kedi", "Ayşe Yılmaz", "05321234567"]]),
            ("Aciklama", [["Not"], ["okunmaz"]]));

        var response = await PostAsync(client, Guid.NewGuid(), "Pilot", null, ("file", "veri.xlsx", bytes, XlsxType));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = json.GetProperty("id").GetGuid();
        json.GetProperty("status").GetString().Should().Be("Uploaded");
        json.GetProperty("summary").GetProperty("customers").GetProperty("total").GetInt32().Should().Be(2);
        json.GetProperty("summary").GetProperty("pets").GetProperty("total").GetInt32().Should().Be(1);
        json.GetProperty("summary").GetProperty("vaccinations").GetProperty("total").GetInt32().Should().Be(0);
        json.GetProperty("warnings").GetArrayLength().Should().Be(0);

        var tables = json.GetProperty("tables").EnumerateArray().ToList();
        tables.Select(t => t.GetProperty("table").GetString()).Should().Equal("customers", "pets");
        var customerColumns = tables[0].GetProperty("columns").EnumerateArray().ToList();
        customerColumns.Select(c => c.GetProperty("source").GetString()).Should().Equal("Ad Soyad", "Cep", "E-posta");
        customerColumns.Select(c => c.GetProperty("suggested").GetString()).Should().Equal("AdSoyad", "Telefon", "Eposta");
        customerColumns.Select(c => c.GetProperty("confidence").GetString()).Should().Equal("Exact", "Alias", "Exact");
        customerColumns.Should().OnlyContain(c => c.GetProperty("mapped").ValueKind == JsonValueKind.Null);
        tables[0].GetProperty("rowCount").GetInt32().Should().Be(2);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var batch = await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == id);
        batch.ContentHash.Should().Be(Convert.ToHexStringLower(SHA256.HashData(bytes)));
        batch.CustomerCounts.Total.Should().Be(2);
        var rows = await db.ImportBatchRows.AsNoTracking().Where(r => r.BatchId == id).OrderBy(r => r.Table).ThenBy(r => r.RowNumber).ToListAsync();
        rows.Should().HaveCount(3);
        rows[0].RowNumber.Should().Be(2);
        rows[0].SourceJson.Should().Contain("Ayşe Yılmaz").And.Contain("\"Cep\":\"05321234567\"");
        rows[1].SourceJson.Should().Contain("\"Cep\":null");
        rows.Should().OnlyContain(r => r.ValidationStatus == ImportRowValidationStatus.Pending
                                       && r.ApplyStatus == ImportRowApplyStatus.Pending
                                       && r.NormalizedJson == null);
    }

    [Theory]
    // Yeni şablon (ADR-022 "hasta" başlıkları) ve eski şablon aynı API anahtarlarına kesin eşlenir.
    [InlineData("Hastalar", "HastaNo", "HastaAdi")]
    [InlineData("Hayvanlar", "HayvanNo", "HayvanAdi")]
    public async Task Template_Pet_Headers_Old_And_New_Should_Suggest_Same_Api_Keys_And_Map(string sheet, string noHeader, string nameHeader)
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var bytes = Workbook((sheet, [["MusteriNo", noHeader, nameHeader, "Tur"], ["M1", $"H-{sheet}", "Pamuk", "Kedi"]]));

        var response = await PostAsync(client, Guid.NewGuid(), null, null, ("file", "veri.xlsx", bytes, XlsxType));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var columns = json.GetProperty("tables").EnumerateArray().Single().GetProperty("columns").EnumerateArray().ToList();
        columns.Select(c => c.GetProperty("suggested").GetString()).Should().Equal("MusteriNo", "HayvanNo", "HayvanAdi", "Tur");
        columns.Should().OnlyContain(c => c.GetProperty("confidence").GetString() == "Exact");

        var mapped = await PutMappingAsync(client, json.GetProperty("id").GetGuid(), new
        {
            columns = new { pets = Map("MusteriNo", "MusteriNo", "HayvanNo", noHeader, "HayvanAdi", nameHeader, "Tur", "Tur") },
        });

        mapped.StatusCode.Should().Be(HttpStatusCode.OK, await mapped.Content.ReadAsStringAsync());
        var mappedColumns = (await mapped.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("tables")[0].GetProperty("columns").EnumerateArray().ToList();
        mappedColumns.Select(c => c.GetProperty("mapped").GetString()).Should().Equal("MusteriNo", "HayvanNo", "HayvanAdi", "Tur");
    }

    [Fact]
    public async Task Csv_Upload_Should_Read_Windows1254_With_Three_Tables_And_Clinic()
    {
        var ctx = await SeedAsync();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp1254 = Encoding.GetEncoding(1254);
        var client = ClientFor(ctx.Token);

        var response = await PostAsync(
            client, Guid.NewGuid(), null, ctx.ClinicId,
            ("customersFile", "m.csv", cp1254.GetBytes("Ad Soyad;Telefon\nŞükrü Işık;05321234567\n"), "text/csv"),
            ("petsFile", "h.csv", cp1254.GetBytes("Hayvan Adı;Tür;Sahip;Sahip Tel\nÇiçek;Kedi;Şükrü Işık;05321234567\n"), "text/csv"),
            ("vaccinationsFile", "a.csv", cp1254.GetBytes("Hayvan Adı;Tür;Sahip;Sahip Tel;Aşı;Tarih\nÇiçek;Kedi;Şükrü Işık;05321234567;Kuduz;10.04.2024\n"), "text/csv"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("tables").GetArrayLength().Should().Be(3);
        json.GetProperty("warnings").GetArrayLength().Should().Be(0);
        var id = json.GetProperty("id").GetGuid();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var first = await db.ImportBatchRows.AsNoTracking().SingleAsync(r => r.BatchId == id && r.Table == ImportTable.Customers);
        first.SourceJson.Should().Contain("Şükrü Işık");
    }

    [Fact]
    public async Task Vaccination_Table_Without_Clinic_Should_Be_Rejected_And_Nothing_Persisted()
    {
        var ctx = await SeedAsync();

        var response = await PostAsync(
            ClientFor(ctx.Token), Guid.NewGuid(), null, null,
            ("vaccinationsFile", "a.csv", Encoding.UTF8.GetBytes("Aşı;Tarih\nKuduz;10.04.2024\n"), "text/csv"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be("Imports.ClinicRequired");
        (await BatchCountAsync(ctx.TenantId)).Should().Be(0);
    }

    [Fact]
    public async Task Same_File_Twice_Should_Conflict_With_BatchId_And_Be_Allowed_After_Discard()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var bytes = Workbook(("Musteriler", [["AdSoyad"], ["Ayşe"]]));

        var first = await PostAsync(client, Guid.NewGuid(), null, null, ("file", "veri.xlsx", bytes, XlsxType));
        var firstId = await IdAsync(first);
        var second = await PostAsync(client, Guid.NewGuid(), null, null, ("file", "kopya.xlsx", bytes, XlsxType));

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await second.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("Imports.DuplicateFile");
        problem.GetProperty("batchId").GetGuid().Should().Be(firstId);
        (await BatchCountAsync(ctx.TenantId)).Should().Be(1);

        (await client.DeleteAsync($"/api/v1/imports/batches/{firstId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await PostAsync(client, Guid.NewGuid(), null, null, ("file", "tekrar.xlsx", bytes, XlsxType)))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Duplicate_File_Should_Not_Disclose_BatchId_Of_Hidden_Clinic_Batch()
    {
        var ctx = await SeedAsync();
        var otherClinicId = await SeedClinicAsync(ctx.TenantId);
        var bytes = Workbook(("Musteriler", [["AdSoyad"], ["Gizli"]]));
        await SeedBatchWithHashAsync(ctx.TenantId, otherClinicId, Convert.ToHexStringLower(SHA256.HashData(bytes)));

        var response = await PostAsync(ClientFor(ctx.Token), Guid.NewGuid(), null, null, ("file", "veri.xlsx", bytes, XlsxType));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("Imports.DuplicateFile");
        problem.TryGetProperty("batchId", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Same_RequestId_And_File_Should_Replay_With_Tables_And_Different_File_Should_Conflict()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var requestId = Guid.NewGuid();
        var bytes = Workbook(("Musteriler", [["Ad Soyad"], ["Ayşe"]]));
        var other = Workbook(("Musteriler", [["Ad Soyad"], ["Başka"]]));

        var first = await PostAsync(client, requestId, "Parti", null, ("file", "a.xlsx", bytes, XlsxType));
        var replay = await PostAsync(client, requestId, "Parti", null, ("file", "adi-farkli.xlsx", bytes, XlsxType));
        var conflict = await PostAsync(client, requestId, "Parti", null, ("file", "a.xlsx", other, XlsxType));

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayJson = await replay.Content.ReadFromJsonAsync<JsonElement>();
        replayJson.GetProperty("id").GetGuid().Should().Be(await IdAsync(first));
        replayJson.GetProperty("tables").GetArrayLength().Should().Be(1);
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(conflict)).Should().Be("Imports.RequestIdConflict");
        (await BatchCountAsync(ctx.TenantId)).Should().Be(1);
    }

    [Fact]
    public async Task Missing_File_Should_Be_Empty_File_Error()
    {
        var ctx = await SeedAsync();

        var response = await PostAsync(ClientFor(ctx.Token), Guid.NewGuid(), "x", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be("Imports.EmptyFile");
        (await BatchCountAsync(ctx.TenantId)).Should().Be(0);
    }

    [Theory]
    [InlineData("veri.xls", "application/vnd.ms-excel")]
    [InlineData("veri.xlsm", "application/vnd.ms-excel.sheet.macroEnabled.12")]
    [InlineData("veri.xlsx", "application/pdf")]
    public async Task Unsupported_File_Kinds_Should_Be_Rejected(string fileName, string contentType)
    {
        var ctx = await SeedAsync();
        var bytes = Workbook(("Musteriler", [["AdSoyad"], ["Ayşe"]]));

        var response = await PostAsync(ClientFor(ctx.Token), Guid.NewGuid(), null, null, ("file", fileName, bytes, contentType));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be("Imports.UnsupportedFile");
        (await BatchCountAsync(ctx.TenantId)).Should().Be(0);
    }

    [Fact]
    public async Task Corrupt_Xlsx_Should_Be_Rejected_Without_Leaving_A_Batch()
    {
        var ctx = await SeedAsync();
        var valid = Workbook(("Musteriler", [["AdSoyad"], ["Ayşe"]]));

        var response = await PostAsync(
            ClientFor(ctx.Token), Guid.NewGuid(), null, null, ("file", "veri.xlsx", valid[..(valid.Length / 2)], XlsxType));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be("Imports.UnsupportedFile");
        (await BatchCountAsync(ctx.TenantId)).Should().Be(0);
    }

    [Fact]
    public async Task Invalid_Encoding_Or_Delimiter_Should_Be_Validation_Error()
    {
        var ctx = await SeedAsync();
        var csv = ("customersFile", "m.csv", Encoding.UTF8.GetBytes("Ad;Tel\nA;1\n"), "text/csv");

        var encoding = await PostAsync(ClientFor(ctx.Token), Guid.NewGuid(), null, null, new[] { ("encoding", "latin5") }, csv);
        var delimiter = await PostAsync(ClientFor(ctx.Token), Guid.NewGuid(), null, null, new[] { ("delimiter", "|") }, csv);

        encoding.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        delimiter.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await BatchCountAsync(ctx.TenantId)).Should().Be(0);
    }

    [Fact]
    public async Task Forced_Encoding_Mismatch_Should_Return_Encoding_Suspect_Warning()
    {
        var ctx = await SeedAsync();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(1254).GetBytes("Ad Soyad;Telefon\nŞükrü;1\n");

        var response = await PostAsync(
            ClientFor(ctx.Token), Guid.NewGuid(), null, null,
            new[] { ("encoding", "utf-8") },
            ("customersFile", "m.csv", bytes, "text/csv"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var warnings = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("warnings").EnumerateArray().ToList();
        warnings.Should().ContainSingle();
        warnings[0].GetProperty("code").GetString().Should().Be("Imports.EncodingSuspect");
        warnings[0].GetProperty("table").GetString().Should().Be("customers");
    }

    [Fact]
    public async Task Upload_Audit_Should_Not_Contain_File_Content_Or_Row_Data()
    {
        var ctx = await SeedAsync();
        var requestId = Guid.NewGuid();
        var bytes = Workbook(("Musteriler", [["AdSoyad", "Telefon"], ["Gizli Kişi", "05329998877"]]));

        (await PostAsync(ClientFor(ctx.Token), requestId, "Denetim", null, ("file", "veri.xlsx", bytes, XlsxType)))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = await db.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.Action == "ImportBatch.Create" && a.TargetId!.Contains(requestId.ToString()));
        audit.RequestPayload.Should().NotBeNull();
        audit.RequestPayload.Should().Contain("Denetim").And.NotContain("Gizli").And.NotContain("05329998877")
            .And.NotContain("UEs").And.NotContain("files").And.NotContain("Files");
        audit.RequestPayload!.Length.Should().BeLessThan(600);
    }

    [Fact]
    public async Task Table_At_Row_Limit_Should_Upload_And_One_Over_Should_Be_Rejected_Without_Leftovers()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var salt = Guid.NewGuid().ToString("N");

        byte[] Csv(int rows) => Encoding.UTF8.GetBytes(
            "Ad Soyad;Telefon\n" + string.Join('\n', Enumerable.Range(1, rows).Select(i => $"Kişi {salt} {i};05321234567")));

        var over = await PostAsync(client, Guid.NewGuid(), null, null, ("customersFile", "m.csv", Csv(10_001), "text/csv"));
        over.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(over)).Should().Be("Imports.TooManyRows");
        (await BatchCountAsync(ctx.TenantId)).Should().Be(0);

        var ok = await PostAsync(client, Guid.NewGuid(), null, null, ("customersFile", "m.csv", Csv(10_000), "text/csv"));
        ok.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await IdAsync(ok);
        (await RowCountAsync(id)).Should().Be(10_000);
    }

    // ---------------------------------------------------------------- Okuma

    [Fact]
    public async Task Get_Should_Return_Tables_And_List_Should_Not()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var id = await UploadCustomersAsync(client);

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/imports/batches/{id}");
        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/imports/batches");

        detail.GetProperty("tables").GetArrayLength().Should().Be(1);
        detail.GetProperty("tables")[0].GetProperty("columns").GetArrayLength().Should().Be(3);
        list.GetProperty("items")[0].GetProperty("tables").ValueKind.Should().Be(JsonValueKind.Null);
        list.GetProperty("items")[0].GetProperty("warnings").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ---------------------------------------------------------------- Eşleme

    [Fact]
    public async Task Mapping_Should_Be_Saved_Move_Batch_To_Mapped_And_Show_In_Tables()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var id = await UploadCustomersAsync(client);

        var response = await PutMappingAsync(client, id, new
        {
            columns = new { customers = Map("AdSoyad", "Müşteri", "Telefon", "GSM") },
            options = new { onDuplicateClient = "Skip" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("status").GetString().Should().Be("Mapped");
        json.GetProperty("validationId").ValueKind.Should().Be(JsonValueKind.Null);
        var columns = json.GetProperty("tables")[0].GetProperty("columns").EnumerateArray().ToList();
        columns.Select(c => (c.GetProperty("source").GetString(), c.GetProperty("mapped").GetString())).Should().Equal(
            ("Müşteri", "AdSoyad"), ("GSM", "Telefon"), ("Mail", null));

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var batch = await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == id);
        batch.Status.Should().Be(ImportBatchStatus.Mapped);
        batch.OptionsJson.Should().Contain("Skip");
        batch.MappingJson.Should().Contain("AdSoyad");

        // Tekrar eşleme tam değiştirmedir; seçenek varsayılana döner.
        var again = await PutMappingAsync(client, id, new { columns = new { customers = Map("AdSoyad", "Müşteri") } });
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterJson = await again.Content.ReadFromJsonAsync<JsonElement>();
        afterJson.GetProperty("tables")[0].GetProperty("columns").EnumerateArray()
            .Count(c => c.GetProperty("mapped").ValueKind != JsonValueKind.Null).Should().Be(1);
    }

    [Fact]
    public async Task Mapping_With_Missing_Required_Field_Should_Be_Rejected_And_Leave_Batch_Unchanged()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var id = await UploadCustomersAsync(client);

        var response = await PutMappingAsync(client, id, new { columns = new { customers = Map("Telefon", "GSM") } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be("Imports.MappingIncomplete");
        (await StatusAsync(id)).Should().Be(ImportBatchStatus.Uploaded);
    }

    [Fact]
    public async Task Mapping_Without_Columns_For_Present_Table_Should_Be_Incomplete()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var id = await UploadCustomersAsync(client);

        var response = await PutMappingAsync(client, id, new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be("Imports.MappingIncomplete");
    }

    [Theory]
    [InlineData("{\"columns\":{\"customers\":{\"AdSoyad\":\"Müşteri\",\"Telefon\":\"Yok\"}}}")]
    [InlineData("{\"columns\":{\"customers\":{\"AdSoyad\":\"Müşteri\",\"Bilinmeyen\":\"GSM\"}}}")]
    [InlineData("{\"columns\":{\"pets\":{\"HayvanAdi\":\"Müşteri\"},\"customers\":{\"AdSoyad\":\"Müşteri\"}}}")]
    [InlineData("{\"columns\":{\"customers\":{\"adsoyad\":\"Müşteri\"}}}")]
    public async Task Mapping_With_Unknown_Table_Field_Or_Column_Should_Be_Rejected(string body)
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var id = await UploadCustomersAsync(client);

        var response = await PutRawMappingAsync(client, id, body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be("Imports.MappingUnknownColumn");
        (await StatusAsync(id)).Should().Be(ImportBatchStatus.Uploaded);
    }

    [Fact]
    public async Task Same_Source_Column_Mapped_Twice_Should_Be_Rejected()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var id = await UploadCustomersAsync(client);

        var response = await PutMappingAsync(client, id, new { columns = new { customers = Map("AdSoyad", "Müşteri", "Adres", "Müşteri") } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be("Imports.MappingSourceReused");
    }

    [Fact]
    public async Task Invalid_Options_Or_Value_Mapping_Shape_Should_Be_Validation_Errors()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var id = await UploadCustomersAsync(client);
        var columns = new { customers = Map("AdSoyad", "Müşteri") };

        var options = await PutMappingAsync(client, id, new { columns, options = new { onDuplicateClient = "Boş ver" } });
        var kind = await PutMappingAsync(client, id, new { columns, valueMappings = new[] { new { kind = "x", source = "a", targetId = (Guid?)null } } });
        var source = await PutMappingAsync(client, id, new { columns, valueMappings = new[] { new { kind = "species", source = " ", targetId = (Guid?)null } } });

        options.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        kind.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        source.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await StatusAsync(id)).Should().Be(ImportBatchStatus.Uploaded);
    }

    [Fact]
    public async Task Value_Mappings_Should_Be_Validated_Against_Active_Visible_Catalog()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var id = await UploadCustomersAsync(client);
        var catalog = await SeedCatalogAsync(ctx.TenantId, (await SeedAsync()).TenantId);
        var columns = new { customers = Map("AdSoyad", "Müşteri") };

        var ok = await PutMappingAsync(client, id, new
        {
            columns,
            valueMappings = new object[]
            {
                new { kind = "species", source = "Kopek", targetId = catalog.SpeciesId },
                new { kind = "vaccine", source = "Kuduz-2023", targetId = catalog.TenantVaccineId },
                new { kind = "vaccine", source = "Bilmiyorum", targetId = (Guid?)null },
            },
        });
        var missing = await PutMappingAsync(client, id, new
        {
            columns, valueMappings = new[] { new { kind = "species", source = "Kopek", targetId = (Guid?)Guid.NewGuid() } },
        });
        var inactive = await PutMappingAsync(client, id, new
        {
            columns, valueMappings = new[] { new { kind = "vaccine", source = "Pasif", targetId = (Guid?)catalog.InactiveVaccineId } },
        });
        var foreign = await PutMappingAsync(client, id, new
        {
            columns, valueMappings = new[] { new { kind = "vaccine", source = "Yabancı", targetId = (Guid?)catalog.ForeignVaccineId } },
        });
        var wrongKind = await PutMappingAsync(client, id, new
        {
            columns, valueMappings = new[] { new { kind = "color", source = "Kopek", targetId = (Guid?)catalog.SpeciesId } },
        });
        var duplicate = await PutMappingAsync(client, id, new
        {
            columns,
            valueMappings = new[]
            {
                new { kind = "species", source = "Kopek", targetId = (Guid?)catalog.SpeciesId },
                new { kind = "species", source = "KÖPEK", targetId = (Guid?)null },
            },
        });

        ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        foreach (var rejected in new[] { missing, inactive, foreign, wrongKind, duplicate })
        {
            rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await CodeAsync(rejected)).Should().Be("Imports.ValueMappingTargetInvalid");
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.ImportBatches.AsNoTracking().Where(b => b.Id == id).Select(b => b.MappingJson).SingleAsync();
        saved.Should().Contain(catalog.SpeciesId.ToString()).And.Contain("Kuduz-2023");
    }

    [Fact]
    public async Task Mapping_Should_Be_Rejected_For_Discarded_Batch_And_Audit_Should_Not_Contain_Mapping_Body()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var id = await UploadCustomersAsync(client);
        var columns = new { customers = Map("AdSoyad", "Müşteri") };
        (await PutMappingAsync(client, id, new { columns, valueMappings = new[] { new { kind = "species", source = "GizliDeğer", targetId = (Guid?)null } } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.DeleteAsync($"/api/v1/imports/batches/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var afterDiscard = await PutMappingAsync(client, id, new { columns });

        afterDiscard.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(afterDiscard)).Should().Be("Imports.InvalidState");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "ImportBatch.Mapping" && a.TargetId == $"ImportBatchId={id}")
            .OrderBy(a => a.OccurredAtUtc).ToListAsync();
        audit.Should().HaveCount(2);
        audit.Should().OnlyContain(a => a.RequestPayload != null && !a.RequestPayload.Contains("GizliDeğer") && !a.RequestPayload.Contains("AdSoyad"));
        audit.Select(a => a.Success).Should().Equal(true, false);
    }

    [Fact]
    public async Task Mapping_Another_Tenants_Batch_Should_Be_Not_Found_And_Without_Permission_Forbidden()
    {
        var ctx = await SeedAsync();
        var foreign = await SeedAsync();
        var foreignId = await UploadCustomersAsync(ClientFor(foreign.Token));
        var ownId = await UploadCustomersAsync(ClientFor(ctx.Token));
        var body = new { columns = new { customers = Map("AdSoyad", "Müşteri") } };

        var notFound = await PutMappingAsync(ClientFor(ctx.Token), foreignId, body);
        var readOnly = await PutMappingAsync(ClientFor(await TokenAsync(ctx, new[] { "Imports.Read" })), ownId, body);
        var none = await PutMappingAsync(ClientFor(await TokenAsync(ctx, new[] { "Clients.Read" })), ownId, body);

        notFound.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(notFound)).Should().Be("Imports.BatchNotFound");
        readOnly.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        none.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await StatusAsync(foreignId)).Should().Be(ImportBatchStatus.Uploaded);
        (await StatusAsync(ownId)).Should().Be(ImportBatchStatus.Uploaded);
    }

    // ---------------------------------------------------------------- Taslak silme

    [Fact]
    public async Task Discard_Should_Delete_Staging_Rows_Mark_Purged_And_Keep_Tables_Without_Columns()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var id = await UploadCustomersAsync(client);
        (await RowCountAsync(id)).Should().Be(2);

        (await client.DeleteAsync($"/api/v1/imports/batches/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await RowCountAsync(id)).Should().Be(0);
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/imports/batches/{id}");
        detail.GetProperty("status").GetString().Should().Be("Discarded");
        detail.GetProperty("payloadPurgedAtUtc").ValueKind.Should().NotBe(JsonValueKind.Null);
        var table = detail.GetProperty("tables")[0];
        table.GetProperty("rowCount").GetInt32().Should().Be(2);
        table.GetProperty("columns").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Discard_Should_Not_Delete_Another_Batchs_Rows()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var first = await UploadCustomersAsync(client);
        var second = await UploadCustomersAsync(client);

        (await client.DeleteAsync($"/api/v1/imports/batches/{first}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await RowCountAsync(first)).Should().Be(0);
        (await RowCountAsync(second)).Should().Be(2);
    }

    // ---------------------------------------------------------------- Şablon

    [Fact]
    public async Task Template_Xlsx_Should_Be_Downloadable_With_Read_Permission_And_Not_Cached()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(await TokenAsync(ctx, new[] { "Imports.Read" }));

        var response = await client.GetAsync("/api/v1/imports/template?format=xlsx");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be(XlsxType);
        response.Content.Headers.ContentDisposition!.FileNameStar.Should().Be("vetinity-iceaktar-sablon.xlsx");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        workbook.Worksheets.Select(w => w.Name).Should().Equal("Musteriler", "Hastalar", "Asilar", "Aciklama");
    }

    [Theory]
    [InlineData("customers", "vetinity-iceaktar-musteriler.csv", "MusteriNo;AdSoyad;Telefon;Eposta;Adres")]
    [InlineData("pets", "vetinity-iceaktar-hastalar.csv", "MusteriNo;SahipAdSoyad")]
    [InlineData("vaccinations", "vetinity-iceaktar-asilar.csv", "HastaNo;SahipAdSoyad")]
    public async Task Template_Csv_Should_Be_Downloadable_Per_Table(string table, string fileName, string headerPrefix)
    {
        var ctx = await SeedAsync();

        var response = await ClientFor(ctx.Token).GetAsync($"/api/v1/imports/template?format=csv&table={table}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        response.Content.Headers.ContentDisposition!.FileNameStar.Should().Be(fileName);
        var text = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync());
        text.TrimStart('﻿').Should().StartWith(headerPrefix);
    }

    [Theory]
    [InlineData("?format=pdf")]
    [InlineData("")]
    [InlineData("?format=csv")]
    [InlineData("?format=csv&table=staff")]
    public async Task Template_With_Invalid_Parameters_Should_Be_Bad_Request(string query)
    {
        var ctx = await SeedAsync();

        var response = await ClientFor(ctx.Token).GetAsync($"/api/v1/imports/template{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Template_Without_Import_Permission_Should_Be_Forbidden()
    {
        var ctx = await SeedAsync();

        var response = await ClientFor(await TokenAsync(ctx, new[] { "Clients.Read" })).GetAsync("/api/v1/imports/template?format=xlsx");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Filled_Template_Should_Upload_With_Exact_Suggestions_For_Every_Column()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var template = await client.GetByteArrayAsync("/api/v1/imports/template?format=xlsx");
        using var workbook = new XLWorkbook(new MemoryStream(template));
        workbook.Worksheet("Musteriler").Cell(2, 2).Value = "Ayşe Yılmaz";
        using var filled = new MemoryStream();
        workbook.SaveAs(filled);

        var response = await PostAsync(client, Guid.NewGuid(), null, null, ("file", "sablon.xlsx", filled.ToArray(), XlsxType));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var columns = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tables")[0].GetProperty("columns").EnumerateArray();
        columns.Should().OnlyContain(c => c.GetProperty("confidence").GetString() == "Exact");
    }

    // ---------------------------------------------------------------- Yardımcılar

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
                        sheet.Cell(r + 1, c + 1).Value = s;
                }
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        Guid requestId,
        string? name,
        Guid? clinicId,
        params (string Field, string FileName, byte[] Bytes, string ContentType)[] files)
        => PostAsync(client, requestId, name, clinicId, Array.Empty<(string, string)>(), files);

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        Guid requestId,
        string? name,
        Guid? clinicId,
        (string Field, string Value)[] fields,
        params (string Field, string FileName, byte[] Bytes, string ContentType)[] files)
    {
        var form = new MultipartFormDataContent { { new StringContent(requestId.ToString()), "requestId" } };
        if (name is not null)
            form.Add(new StringContent(name), "name");
        if (clinicId is { } c)
            form.Add(new StringContent(c.ToString()), "clinicId");
        foreach (var (field, value) in fields)
            form.Add(new StringContent(value), field);
        foreach (var (field, fileName, bytes, contentType) in files)
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(content, field, fileName);
        }

        return await client.PostAsync("/api/v1/imports/batches", form);
    }

    /// <summary>Kanonik alan adları büyük/küçük harf duyarlıdır; anonim nesne özellikleri camelCase'e çevrildiğinden sözlük kullanılır.</summary>
    private static Dictionary<string, string> Map(params string[] pairs)
        => Enumerable.Range(0, pairs.Length / 2).ToDictionary(i => pairs[2 * i], i => pairs[2 * i + 1]);

    private static Task<HttpResponseMessage> PutMappingAsync(HttpClient client, Guid id, object body)
        => client.PutAsJsonAsync($"/api/v1/imports/batches/{id}/mapping", body);

    private static Task<HttpResponseMessage> PutRawMappingAsync(HttpClient client, Guid id, string json)
        => client.PutAsync(
            $"/api/v1/imports/batches/{id}/mapping",
            new StringContent(json, Encoding.UTF8, "application/json"));

    /// <summary>Üç sütunlu (Müşteri, GSM, Mail) iki satırlık müşteri dosyası; her çağrı benzersiz içerik üretir.</summary>
    private async Task<Guid> UploadCustomersAsync(HttpClient client)
    {
        var bytes = Workbook(("Musteriler",
        [
            ["Müşteri", "GSM", "Mail"],
            [$"Ayşe {Guid.NewGuid():N}", "05321234567", "ayse@example.com"],
            ["Veli Kaya", "05331234567", "veli@example.com"],
        ]));
        return await IdAsync(await PostAsync(client, Guid.NewGuid(), null, null, ("file", "veri.xlsx", bytes, XlsxType)));
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

    private async Task<int> RowCountAsync(Guid batchId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ImportBatchRows.AsNoTracking().CountAsync(r => r.BatchId == batchId);
    }

    private async Task<ImportBatchStatus> StatusAsync(Guid batchId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ImportBatches.AsNoTracking().Where(b => b.Id == batchId).Select(b => b.Status).SingleAsync();
    }

    private async Task SeedBatchWithHashAsync(Guid tenantId, Guid? clinicId, string contentHash)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var batch = ImportBatch.Create(tenantId, clinicId, Guid.NewGuid(), "Tohum", Guid.NewGuid(), DateTime.UtcNow).Value!;
        batch.AttachFile(contentHash, 1, 0, 0);
        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync();
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

    private sealed record CatalogIds(Guid SpeciesId, Guid TenantVaccineId, Guid InactiveVaccineId, Guid ForeignVaccineId);

    private async Task<CatalogIds> SeedCatalogAsync(Guid tenantId, Guid otherTenantId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var species = new Species($"IMP{suffix}", $"Imp Tür {suffix}");
        var visible = VaccineDefinition.CreateTenant(tenantId, $"IMPV{suffix}", $"Imp Aşı {suffix}", DateTime.UtcNow);
        var inactive = VaccineDefinition.CreateTenant(tenantId, $"IMPI{suffix}", $"Imp Pasif {suffix}", DateTime.UtcNow);
        inactive.Deactivate(DateTime.UtcNow);
        var foreign = VaccineDefinition.CreateTenant(otherTenantId, $"IMPF{suffix}", $"Imp Yabancı {suffix}", DateTime.UtcNow);

        db.Species.Add(species);
        db.VaccineDefinitions.AddRange(visible, inactive, foreign);
        await db.SaveChangesAsync();
        return new CatalogIds(species.Id, visible.Id, inactive.Id, foreign.Id);
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

        var tenant = new Tenant($"imp-{Guid.NewGuid():N}"[..18], DateTime.UtcNow);
        db.Tenants.Add(tenant);
        // Ice aktarma mekanigi testleri ucretli kiracida kosar; deneme kiracisinda tablo basina 500 satir siniri var (TRIAL-004).
        var subscription = TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14);
        subscription.ActivatePaidPlan(SubscriptionPlanCode.Basic, DateTime.UtcNow);
        db.TenantSubscriptions.Add(subscription);
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
