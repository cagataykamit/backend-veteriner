using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Infrastructure.Persistence;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Imports;

/// <summary>
/// İçe aktarma B3 (gerçek SQL Server/LocalDB, kuru çalıştırma): validate, preview, rows, errors; mükerrer, mevcut müşteri,
/// çip çakışması, hatırlatma etkisi, eşzamanlılık/kira, yetki ve kiracı yalıtımı. Sentetik veri; ürün tablolarına yazılmaz.
/// </summary>
[Collection("imports-api")]
public sealed class ImportValidationEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] ManagePermissions = { "Imports.Read", "Imports.Manage" };
    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly CustomWebApplicationFactory _factory;

    public ImportValidationEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    // ================================================================ Ana senaryo

    [Fact]
    public async Task Validate_Should_Fill_Rows_And_Counts_Without_Writing_Product_Tables()
    {
        var ctx = await SeedAsync();
        var cat = await SeedCatalogAsync(ctx.TenantId);
        var client = ClientFor(ctx.Token);
        var id = await UploadMainScenarioAsync(client, ctx, cat);
        await MapIdentityAsync(client, id);

        var json = await ValidateAsync(client, id);

        json.GetProperty("status").GetString().Should().Be("Validated");
        json.GetProperty("validationId").ValueKind.Should().Be(JsonValueKind.String);
        json.GetProperty("validatedAtUtc").ValueKind.Should().Be(JsonValueKind.String);

        Counts(json, "customers").Should().Equal(4, 1, 2, 1, 2, 0, 1); // total, valid, warning, error, create, match, skip
        Counts(json, "pets").Should().Equal(5, 2, 1, 2, 2, 0, 1);
        Counts(json, "vaccinations").Should().Equal(6, 4, 0, 2, 3, 0, 1);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var batch = await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == id);
        batch.Status.Should().Be(ImportBatchStatus.Validated);
        batch.LeaseOwner.Should().BeNull();
        batch.LeaseUntilUtc.Should().BeNull();
        batch.ValidationId.Should().NotBeNull();

        var rows = await db.ImportBatchRows.AsNoTracking().Where(r => r.BatchId == id).ToListAsync();
        rows.Should().NotContain(r => r.ValidationStatus == ImportRowValidationStatus.Pending);
        rows.Should().OnlyContain(r => r.NormalizedJson != null);
        rows.Should().OnlyContain(r => r.ApplyStatus == ImportRowApplyStatus.Pending && r.TargetEntityId == null);

        // Kuru çalıştırma: ürün tablolarına hiçbir şey yazılmadı.
        (await db.Clients.CountAsync(c => c.TenantId == ctx.TenantId)).Should().Be(0);
        (await db.Pets.CountAsync(p => p.TenantId == ctx.TenantId)).Should().Be(0);
        (await db.Vaccinations.CountAsync(v => v.TenantId == ctx.TenantId)).Should().Be(0);
    }

    [Fact]
    public async Task Validate_Twice_Should_Be_Idempotent_With_New_ValidationId()
    {
        var ctx = await SeedAsync();
        var cat = await SeedCatalogAsync(ctx.TenantId);
        var client = ClientFor(ctx.Token);
        var id = await UploadMainScenarioAsync(client, ctx, cat);
        await MapIdentityAsync(client, id);

        var first = await ValidateAsync(client, id);
        var second = await ValidateAsync(client, id);

        second.GetProperty("validationId").GetGuid().Should().NotBe(first.GetProperty("validationId").GetGuid());
        Counts(second, "pets").Should().Equal(Counts(first, "pets"));
        (await RowCountAsync(id)).Should().Be(15);
    }

    [Fact]
    public async Task Preview_Should_Report_Summary_Unmapped_Values_Blockers_And_Reminder_Impact()
    {
        var ctx = await SeedAsync();
        var cat = await SeedCatalogAsync(ctx.TenantId);
        var client = ClientFor(ctx.Token);
        var id = await UploadMainScenarioAsync(client, ctx, cat);
        await MapIdentityAsync(client, id);
        await ValidateAsync(client, id);

        var response = await client.GetAsync($"/api/v1/imports/batches/{id}/preview");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("status").GetString().Should().Be("Validated");
        json.GetProperty("payloadPurged").GetBoolean().Should().BeFalse();

        var unmapped = json.GetProperty("unmappedValues").EnumerateArray().ToList();
        unmapped.Select(u => (u.GetProperty("kind").GetString(), u.GetProperty("source").GetString(), u.GetProperty("rowCount").GetInt32(), u.GetProperty("blocking").GetBoolean()))
            .Should().BeEquivalentTo(new[]
            {
                ("species", "Ejderha", 1, true),
                ("vaccine", "Bilinmeyen Aşı", 1, true),
            });

        json.GetProperty("blockers").EnumerateArray().Select(b => b.GetString())
            .Should().BeEquivalentTo("Imports.SpeciesUnmapped", "Imports.VaccineUnmapped");
        json.GetProperty("canCommit").GetBoolean().Should().BeFalse();

        var impact = json.GetProperty("reminderImpact");
        impact.GetProperty("vaccinationsWithDueDate").GetInt32().Should().Be(3);
        impact.GetProperty("vaccinationsWithFutureDueDate").GetInt32().Should().Be(2);
        impact.GetProperty("vaccinationsWithPastDueDate").GetInt32().Should().Be(1);
        impact.GetProperty("clientsWithEmail").GetInt32().Should().Be(1, "yalnız M1 e-postalı");
        impact.GetProperty("emailsWithinReminderWindow").GetInt32().Should().Be(1);
        impact.GetProperty("reminderWindowDays").GetInt32().Should().Be(3, "kiracı ayarı kaydı yok: varsayılan");
        impact.GetProperty("tenantRemindersEnabled").GetBoolean().Should().BeFalse();
        impact.GetProperty("excludedByDefault").GetBoolean().Should().BeTrue();

        var issueCounts = json.GetProperty("issueCounts").EnumerateArray()
            .ToDictionary(i => i.GetProperty("code").GetString()!, i => i.GetProperty("rowCount").GetInt32());
        issueCounts["Imports.DuplicateInFile"].Should().Be(2);
        issueCounts["Imports.PhoneInvalid"].Should().Be(1);
        issueCounts["Imports.DuplicateVaccination"].Should().Be(1);
        issueCounts["Imports.OwnerRowInvalid"].Should().Be(1);
        issueCounts["Imports.PetRowInvalid"].Should().Be(1);
    }

    [Fact]
    public async Task Preview_CanCommit_Should_Be_True_When_Unmapped_Values_Are_Explicitly_Skipped()
    {
        var ctx = await SeedAsync();
        var cat = await SeedCatalogAsync(ctx.TenantId);
        var client = ClientFor(ctx.Token);
        var id = await UploadMainScenarioAsync(client, ctx, cat);
        await PutMappingAsync(client, id, IdentityColumns(), new object[]
        {
            new { kind = "species", source = "Ejderha", targetId = (Guid?)null },
            new { kind = "vaccine", source = "Bilinmeyen Aşı", targetId = (Guid?)null },
        });
        await ValidateAsync(client, id);

        var json = await (await client.GetAsync($"/api/v1/imports/batches/{id}/preview")).Content.ReadFromJsonAsync<JsonElement>();

        json.GetProperty("blockers").GetArrayLength().Should().Be(0);
        json.GetProperty("canCommit").GetBoolean().Should().BeTrue();
        json.GetProperty("unmappedValues").GetArrayLength().Should().Be(0);
        json.GetProperty("issueCounts").EnumerateArray().Select(i => i.GetProperty("code").GetString())
            .Should().Contain("Imports.ValueSkipped");
    }

    [Fact]
    public async Task Rows_Should_Filter_By_Table_Status_And_Code_And_Page()
    {
        var ctx = await SeedAsync();
        var cat = await SeedCatalogAsync(ctx.TenantId);
        var client = ClientFor(ctx.Token);
        var id = await UploadMainScenarioAsync(client, ctx, cat);
        await MapIdentityAsync(client, id);
        await ValidateAsync(client, id);

        var all = await GetJsonAsync(client, $"/api/v1/imports/batches/{id}/rows?pageSize=200");
        all.GetProperty("totalItems").GetInt32().Should().Be(15);
        all.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("table").GetString())
            .Should().Equal(Enumerable.Repeat("customers", 4).Concat(Enumerable.Repeat("pets", 5)).Concat(Enumerable.Repeat("vaccinations", 6)));

        var petErrors = await GetJsonAsync(client, $"/api/v1/imports/batches/{id}/rows?table=pets&status=Error");
        petErrors.GetProperty("totalItems").GetInt32().Should().Be(2);
        petErrors.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("rowNumber").GetInt32()).Should().Equal(4, 5);

        var byCode = await GetJsonAsync(client, $"/api/v1/imports/batches/{id}/rows?code=Imports.OwnerRowInvalid");
        var only = byCode.GetProperty("items").EnumerateArray().Single();
        only.GetProperty("table").GetString().Should().Be("pets");
        only.GetProperty("rowNumber").GetInt32().Should().Be(5);
        only.GetProperty("validation").GetProperty("status").GetString().Should().Be("Error");
        only.GetProperty("issues")[0].GetProperty("severity").GetString().Should().Be("Error");
        only.GetProperty("source").GetProperty("HayvanAdi").GetString().Should().Be("Hatalı");

        var warnings = await GetJsonAsync(client, $"/api/v1/imports/batches/{id}/rows?table=customers&status=Warning");
        warnings.GetProperty("totalItems").GetInt32().Should().Be(2);

        var created = warnings.GetProperty("items").EnumerateArray().First();
        created.GetProperty("values").GetProperty("action").GetString().Should().Be("Create");
        created.GetProperty("validation").GetProperty("action").GetString().Should().Be("Create");

        var page = await GetJsonAsync(client, $"/api/v1/imports/batches/{id}/rows?table=vaccinations&page=2&pageSize=4");
        page.GetProperty("items").GetArrayLength().Should().Be(2);
        page.GetProperty("totalPages").GetInt32().Should().Be(2);

        var response = await client.GetAsync($"/api/v1/imports/batches/{id}/rows");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Theory]
    [InlineData("table=foo")]
    [InlineData("status=Bogus")]
    [InlineData("code=Imports.NoSuchCode")]
    [InlineData("pageSize=201")]
    [InlineData("page=0")]
    public async Task Rows_Should_Reject_Invalid_Query(string query)
    {
        var ctx = await SeedAsync();
        var cat = await SeedCatalogAsync(ctx.TenantId);
        var client = ClientFor(ctx.Token);
        var id = await UploadMainScenarioAsync(client, ctx, cat);
        await MapIdentityAsync(client, id);
        await ValidateAsync(client, id);

        var response = await client.GetAsync($"/api/v1/imports/batches/{id}/rows?{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ================================================================ Hata raporu

    [Fact]
    public async Task Errors_Xlsx_Should_Contain_Original_Rows_With_Codes_And_Neutralize_Formulas()
    {
        var ctx = await SeedAsync();
        var cat = await SeedCatalogAsync(ctx.TenantId);
        var client = ClientFor(ctx.Token);
        var id = await UploadMainScenarioAsync(client, ctx, cat);
        await MapIdentityAsync(client, id);
        await ValidateAsync(client, id);

        var response = await client.GetAsync($"/api/v1/imports/batches/{id}/errors?format=xlsx");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be(XlsxType);
        response.Content.Headers.ContentDisposition!.FileName.Should().Be("vetinity-iceaktar-hatalar.xlsx");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();

        using var wb = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        wb.Worksheets.Select(s => s.Name).Should().Equal("Musteriler", "Hayvanlar", "Asilar");

        var customers = wb.Worksheet("Musteriler");
        var header = customers.Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        header.Should().Equal("MusteriNo", "AdSoyad", "Telefon", "Eposta", "Adres", "SatirNo", "Durum", "HataKodu", "Aciklama");
        customers.RowsUsed().Count().Should().Be(2, "başlık + 1 hatalı müşteri");
        customers.Cell(2, 1).GetString().Should().Be("M3");
        customers.Cell(2, 6).GetValue<int>().Should().Be(4);
        customers.Cell(2, 8).GetString().Should().Be("Imports.RequiredMissing");

        // Formül enjeksiyonu: '=' ile başlayan özgün hücre formül değil, metin öneki taşıyan metindir.
        var address = customers.Cell(2, 5);
        address.HasFormula.Should().BeFalse();
        address.DataType.Should().Be(XLDataType.Text);
        address.GetString().Should().StartWith("=HYPERLINK");
        address.Style.IncludeQuotePrefix.Should().BeTrue();

        wb.Worksheet("Hayvanlar").RowsUsed().Count().Should().Be(3, "başlık + 2 hatalı hasta");
        wb.Worksheet("Asilar").RowsUsed().Count().Should().Be(3, "başlık + 2 hatalı aşı");
    }

    [Fact]
    public async Task Errors_Csv_Should_Use_Fixed_Columns_And_Prefix_Formula_Cells()
    {
        var ctx = await SeedAsync();
        var cat = await SeedCatalogAsync(ctx.TenantId);
        var client = ClientFor(ctx.Token);
        var id = await UploadMainScenarioAsync(client, ctx, cat);
        await MapIdentityAsync(client, id);
        await ValidateAsync(client, id);

        var response = await client.GetAsync($"/api/v1/imports/batches/{id}/errors?format=csv");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().Be("Tablo;SatirNo;Durum;HataKodu;Aciklama;Kaynak");
        lines.Should().HaveCount(1 + 1 + 2 + 2);
        text.Should().Contain("Imports.SpeciesUnmapped").And.Contain("Imports.VaccineUnmapped");
        // Kaynak sütunu başlık=değer ile başlar; formül karakteriyle başlayan başlık hücresi ' ile korunur, değer çiftleri metindir.
        text.Should().NotContain(";=").And.NotContain(";+").And.NotContain(";@");
    }

    [Fact]
    public async Task Errors_Should_Reject_Unknown_Format_And_Default_To_Xlsx()
    {
        var ctx = await SeedAsync();
        var cat = await SeedCatalogAsync(ctx.TenantId);
        var client = ClientFor(ctx.Token);
        var id = await UploadMainScenarioAsync(client, ctx, cat);
        await MapIdentityAsync(client, id);
        await ValidateAsync(client, id);

        (await client.GetAsync($"/api/v1/imports/batches/{id}/errors?format=pdf")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var noFormat = await client.GetAsync($"/api/v1/imports/batches/{id}/errors");
        noFormat.Content.Headers.ContentType!.MediaType.Should().Be(XlsxType);
    }

    // ================================================================ Mevcut veri: mükerrer (K4) ve çip (K10)

    [Fact]
    public async Task Existing_Client_And_Pet_Should_Match_And_Microchip_Conflict_Should_Error()
    {
        var ctx = await SeedAsync();
        var cat = await SeedCatalogAsync(ctx.TenantId);
        var sfx = cat.Suffix;
        var (clientId, petId) = await SeedExistingAsync(ctx.TenantId, cat, $"Mevcut {sfx}", "905321230000", "Boncuk", "985112345678999");
        await SeedExistingAsync(ctx.TenantId, cat, $"Diğer {sfx}", "905321239999", "Çipli", "985112345678000");
        var client = ClientFor(ctx.Token);

        var bytes = Workbook(
            ("Musteriler",
            [
                ["AdSoyad", "Telefon"],
                [$"mevcut {sfx}", "0532 123 00 00"],
                [$"Yeni {sfx}", "05329998877"],
            ]),
            ("Hayvanlar",
            [
                ["SahipAdSoyad", "SahipTelefon", "HayvanAdi", "Tur", "Mikrocip"],
                [$"Mevcut {sfx}", "05321230000", "boncuk", cat.SpeciesName, "985112345678999"], // mevcut hasta + aynı çip
                [$"Yeni {sfx}", "05329998877", "Tarçın", cat.SpeciesName, "985 112 345 678 000"], // başka hayvanın çipi
                [$"Mevcut {sfx}", "05321230000", "Yeni Pet", cat.SpeciesName, null], // mevcut müşteriye yeni hasta
            ]));
        var id = await IdAsync(await PostAsync(client, Guid.NewGuid(), null, null, ("file", "mevcut.xlsx", bytes, XlsxType)));
        await PutMappingAsync(client, id, new Dictionary<string, Dictionary<string, string>>
        {
            ["customers"] = Map("AdSoyad", "AdSoyad", "Telefon", "Telefon"),
            ["pets"] = Map("SahipAdSoyad", "SahipAdSoyad", "SahipTelefon", "SahipTelefon", "HayvanAdi", "HayvanAdi", "Tur", "Tur", "Mikrocip", "Mikrocip"),
        });

        await ValidateAsync(client, id);

        var customers = await RowsAsync(client, id, "customers");
        customers[0].GetProperty("validation").GetProperty("action").GetString().Should().Be("Match");
        customers[0].GetProperty("values").GetProperty("targetId").GetGuid().Should().Be(clientId);
        customers[0].GetProperty("issues")[0].GetProperty("code").GetString().Should().Be("Imports.MatchedExisting");
        customers[0].GetProperty("validation").GetProperty("status").GetString().Should().Be("Valid");
        customers[1].GetProperty("validation").GetProperty("action").GetString().Should().Be("Create");

        var pets = await RowsAsync(client, id, "pets");
        pets[0].GetProperty("validation").GetProperty("action").GetString().Should().Be("Match");
        pets[0].GetProperty("values").GetProperty("targetId").GetGuid().Should().Be(petId);
        pets[1].GetProperty("validation").GetProperty("status").GetString().Should().Be("Error");
        pets[1].GetProperty("issues")[0].GetProperty("code").GetString().Should().Be("Imports.MicrochipConflict");
        pets[2].GetProperty("validation").GetProperty("action").GetString().Should().Be("Create");
        pets[2].GetProperty("values").GetProperty("ownerKey").GetString().Should().Be($"db:{clientId}");

        // Mevcut kayıtlar değişmedi.
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Clients.CountAsync(c => c.TenantId == ctx.TenantId)).Should().Be(2);
        (await db.Pets.CountAsync(p => p.TenantId == ctx.TenantId)).Should().Be(2);
    }

    [Fact]
    public async Task Existing_Clients_Found_By_Only_Phone_Or_Only_Name_Should_Both_Be_Considered()
    {
        // Mevcut kayıtlar anahtar türü başına ayrı sorguyla okunur; yalnız telefonla ve yalnız adla bulunanlar birlikte değerlendirilmeli.
        var ctx = await SeedAsync();
        var cat = await SeedCatalogAsync(ctx.TenantId);
        var sfx = cat.Suffix;
        await SeedExistingAsync(ctx.TenantId, cat, $"Başka Kişi {sfx}", "905321230000", "Boncuk", null);
        await SeedExistingAsync(ctx.TenantId, cat, $"ADLI KİŞİ {sfx}", "905321239999", "Pamuk", null);
        var client = ClientFor(ctx.Token);

        var bytes = Workbook(("Musteriler",
        [
            ["AdSoyad", "Telefon"],
            [$"Yeni Kişi {sfx}", "0532 123 00 00"], // telefon başka mevcut müşteride (yalnız telefon sorgusuyla bulunur)
            [$"adlı kişi {sfx}", null], // telefonsuz, adı mevcut müşteriyle aynı (yalnız ad sorgusuyla bulunur)
        ]));
        var id = await IdAsync(await PostAsync(client, Guid.NewGuid(), null, null, ("file", "anahtar.xlsx", bytes, XlsxType)));
        await PutMappingAsync(client, id, new Dictionary<string, Dictionary<string, string>>
        {
            ["customers"] = Map("AdSoyad", "AdSoyad", "Telefon", "Telefon"),
        });

        await ValidateAsync(client, id);

        var customers = await RowsAsync(client, id, "customers");
        customers[0].GetProperty("validation").GetProperty("action").GetString().Should().Be("Create");
        customers[0].GetProperty("issues").EnumerateArray().Select(i => i.GetProperty("code").GetString()).Should().Contain("Imports.SharedPhone");
        customers[1].GetProperty("validation").GetProperty("action").GetString().Should().Be("Create");
        customers[1].GetProperty("issues").EnumerateArray().Select(i => i.GetProperty("code").GetString()).Should().Contain("Imports.PossibleDuplicate");
    }

    [Fact]
    public async Task OnDuplicateClient_Error_Option_Should_Make_Existing_Client_Row_An_Error()
    {
        var ctx = await SeedAsync();
        var cat = await SeedCatalogAsync(ctx.TenantId);
        await SeedExistingAsync(ctx.TenantId, cat, $"Mevcut {cat.Suffix}", "905321230000", "Boncuk", null);
        var client = ClientFor(ctx.Token);
        var bytes = Workbook(("Musteriler", [["AdSoyad", "Telefon"], [$"Mevcut {cat.Suffix}", "05321230000"]]));
        var id = await IdAsync(await PostAsync(client, Guid.NewGuid(), null, null, ("file", "m.xlsx", bytes, XlsxType)));
        var put = await client.PutAsJsonAsync($"/api/v1/imports/batches/{id}/mapping", new
        {
            columns = new Dictionary<string, Dictionary<string, string>> { ["customers"] = Map("AdSoyad", "AdSoyad", "Telefon", "Telefon") },
            options = new { onDuplicateClient = "Error" },
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await ValidateAsync(client, id);

        // Tek satır hatalı: hiç aktarılabilir satır yok.
        json.GetProperty("status").GetString().Should().Be("ValidationFailed");
        Counts(json, "customers").Should().Equal(1, 0, 0, 1, 0, 0, 0);
        var rows = await RowsAsync(client, id, "customers");
        rows[0].GetProperty("issues")[0].GetProperty("code").GetString().Should().Be("Imports.DuplicateClient");
        var preview = await GetJsonAsync(client, $"/api/v1/imports/batches/{id}/preview");
        preview.GetProperty("blockers").EnumerateArray().Select(b => b.GetString()).Should().Equal("Imports.NoImportableRows");
        preview.GetProperty("canCommit").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Existing_Vaccination_Should_Be_Skipped_As_Duplicate()
    {
        var ctx = await SeedAsync();
        var cat = await SeedCatalogAsync(ctx.TenantId);
        var (_, petId) = await SeedExistingAsync(ctx.TenantId, cat, $"Mevcut {cat.Suffix}", "905321230000", "Boncuk", null);
        await SeedVaccinationAsync(ctx, petId, cat.VaccineId, new DateOnly(2024, 4, 10));
        var client = ClientFor(ctx.Token);
        var bytes = Workbook(("Asilar",
        [
            ["SahipAdSoyad", "SahipTelefon", "HayvanAdi", "Tur", "AsiAdi", "UygulamaTarihi"],
            [$"Mevcut {cat.Suffix}", "05321230000", "Boncuk", cat.SpeciesName, cat.VaccineName, "10.04.2024"],
            [$"Mevcut {cat.Suffix}", "05321230000", "Boncuk", cat.SpeciesName, cat.VaccineName, "11.04.2024"],
        ]));
        var id = await IdAsync(await PostAsync(client, Guid.NewGuid(), null, ctx.ClinicId, ("file", "a.xlsx", bytes, XlsxType)));
        await PutMappingAsync(client, id, new Dictionary<string, Dictionary<string, string>>
        {
            ["vaccinations"] = Map(
                "SahipAdSoyad", "SahipAdSoyad", "SahipTelefon", "SahipTelefon", "HayvanAdi", "HayvanAdi",
                "Tur", "Tur", "AsiAdi", "AsiAdi", "UygulamaTarihi", "UygulamaTarihi"),
        });

        var json = await ValidateAsync(client, id);

        json.GetProperty("status").GetString().Should().Be("Validated");
        Counts(json, "vaccinations").Should().Equal(2, 2, 0, 0, 1, 0, 1);
        var rows = await RowsAsync(client, id, "vaccinations");
        rows[0].GetProperty("issues")[0].GetProperty("code").GetString().Should().Be("Imports.DuplicateVaccination");
        rows[1].GetProperty("validation").GetProperty("action").GetString().Should().Be("Create");
    }

    // ================================================================ Dosya düzeyi

    [Fact]
    public async Task Pets_Linked_Only_By_Customer_Number_Without_Customer_Table_Should_Fail_Validation()
    {
        var ctx = await SeedAsync();
        var cat = await SeedCatalogAsync(ctx.TenantId);
        var client = ClientFor(ctx.Token);
        var bytes = Workbook(("Hayvanlar", [["MusteriNo", "HayvanAdi", "Tur"], ["M1", "Pamuk", cat.SpeciesName]]));
        var id = await IdAsync(await PostAsync(client, Guid.NewGuid(), null, null, ("file", "h.xlsx", bytes, XlsxType)));
        await PutMappingAsync(client, id, new Dictionary<string, Dictionary<string, string>>
        {
            ["pets"] = Map("MusteriNo", "MusteriNo", "HayvanAdi", "HayvanAdi", "Tur", "Tur"),
        });

        var json = await ValidateAsync(client, id);

        json.GetProperty("status").GetString().Should().Be("ValidationFailed");
        json.GetProperty("validationId").ValueKind.Should().Be(JsonValueKind.Null);
        Counts(json, "pets").Should().Equal(1, 0, 0, 0, 0, 0, 0);
        var preview = await GetJsonAsync(client, $"/api/v1/imports/batches/{id}/preview");
        preview.GetProperty("blockers").EnumerateArray().Select(b => b.GetString()).Should().Equal("Imports.OwnerLinkUnavailable");
        preview.GetProperty("canCommit").GetBoolean().Should().BeFalse();
        (await StatusAsync(id)).Should().Be(ImportBatchStatus.ValidationFailed);
    }

    [Fact]
    public async Task Revalidation_After_Failure_Should_Be_Allowed_And_Mapping_Change_Should_Invalidate_Results()
    {
        var ctx = await SeedAsync();
        var cat = await SeedCatalogAsync(ctx.TenantId);
        var client = ClientFor(ctx.Token);
        var id = await UploadMainScenarioAsync(client, ctx, cat);
        await MapIdentityAsync(client, id);
        await ValidateAsync(client, id);

        // Eşleme değişince sonuçlar güncel değil: parti Mapped, preview/rows/errors 409.
        await MapIdentityAsync(client, id);
        (await StatusAsync(id)).Should().Be(ImportBatchStatus.Mapped);
        foreach (var path in new[] { "preview", "rows", "errors" })
        {
            var stale = await client.GetAsync($"/api/v1/imports/batches/{id}/{path}");
            stale.StatusCode.Should().Be(HttpStatusCode.Conflict, path);
            (await CodeAsync(stale)).Should().Be("Imports.InvalidState");
        }

        await ValidateAsync(client, id);
        (await client.GetAsync($"/api/v1/imports/batches/{id}/preview")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ================================================================ Durum, eşzamanlılık, kira

    [Fact]
    public async Task Validate_Uploaded_Batch_Without_Mapping_Should_Be_Invalid_State()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var id = await IdAsync(await PostAsync(client, Guid.NewGuid(), null, null,
            ("file", "m.xlsx", Workbook(("Musteriler", [["AdSoyad"], ["Ayşe"]])), XlsxType)));

        var response = await client.PostAsync($"/api/v1/imports/batches/{id}/validate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(response)).Should().Be("Imports.InvalidState");
        (await StatusAsync(id)).Should().Be(ImportBatchStatus.Uploaded);
    }

    [Fact]
    public async Task Validate_Discarded_Batch_Should_Be_Invalid_State()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var id = await IdAsync(await PostAsync(client, Guid.NewGuid(), null, null,
            ("file", "m.xlsx", Workbook(("Musteriler", [["AdSoyad"], ["Ayşe"]])), XlsxType)));
        (await client.DeleteAsync($"/api/v1/imports/batches/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await client.PostAsync($"/api/v1/imports/batches/{id}/validate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(response)).Should().Be("Imports.InvalidState");
    }

    [Fact]
    public async Task Validate_Should_Conflict_When_Another_Batch_Of_Tenant_Is_Running()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var a = await UploadCustomersAsync(client);
        var b = await UploadCustomersAsync(client);
        await MapCustomersAsync(client, a);
        await MapCustomersAsync(client, b);
        await ForceValidatingAsync(b, DateTime.UtcNow);

        var response = await client.PostAsync($"/api/v1/imports/batches/{a}/validate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await CodeAsync(response)).Should().Be("Imports.AnotherJobRunning");
        (await StatusAsync(a)).Should().Be(ImportBatchStatus.Mapped, "veritabanı tek-aktif-iş indeksi ikinciyi reddetti, parti değişmedi");
        (await StatusAsync(b)).Should().Be(ImportBatchStatus.Validating);
    }

    // ================================================================ Arka plan işi (ADR-019 K13)

    [Fact]
    public async Task Validate_Should_Queue_And_Return_202_While_Results_Are_Not_Ready_Until_Worker_Finishes()
    {
        var ctx = await SeedAsync();
        var cat = await SeedCatalogAsync(ctx.TenantId);
        var client = ClientFor(ctx.Token);
        var id = await UploadMainScenarioAsync(client, ctx, cat);
        await MapIdentityAsync(client, id);

        var response = await client.PostAsync($"/api/v1/imports/batches/{id}/validate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        response.Headers.Location.Should().BeNull();
        var queued = await response.Content.ReadFromJsonAsync<JsonElement>();
        queued.GetProperty("status").GetString().Should().Be("Validating");
        queued.GetProperty("validationId").ValueKind.Should().Be(JsonValueKind.Null);

        // Kuyrukta: ilerleme doğrulama fazını verir, işçi almadığı için kira yok ve işlenen 0.
        var progress = await GetJsonAsync(client, $"/api/v1/imports/batches/{id}/progress");
        progress.GetProperty("status").GetString().Should().Be("Validating");
        progress.GetProperty("phase").GetString().Should().Be("Validating");
        progress.GetProperty("processed").GetInt32().Should().Be(0);
        progress.GetProperty("total").GetInt32().Should().Be(15);
        progress.GetProperty("leaseActive").GetBoolean().Should().BeFalse();

        // Sonuç hazır değil: sonuç uçları 409, eşleme/silme/uygulama engelli.
        foreach (var path in new[] { "preview", "rows", "errors" })
        {
            var notReady = await client.GetAsync($"/api/v1/imports/batches/{id}/{path}");
            notReady.StatusCode.Should().Be(HttpStatusCode.Conflict, path);
            (await CodeAsync(notReady)).Should().Be("Imports.InvalidState", path);
        }

        var remap = await client.PutAsJsonAsync($"/api/v1/imports/batches/{id}/mapping", new { columns = IdentityColumns(), valueMappings = Array.Empty<object>() });
        remap.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(remap)).Should().Be("Imports.InvalidState");
        var discard = await client.DeleteAsync($"/api/v1/imports/batches/{id}");
        discard.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(discard)).Should().Be("Imports.InvalidState");
        var committer = ClientFor(await TokenAsync(ctx, new[] { "Imports.Read", "Imports.Manage", "Imports.Commit", "Clients.Create", "Pets.Create", "Vaccinations.Create" }));
        var commit = await committer.PostAsJsonAsync($"/api/v1/imports/batches/{id}/commit", new { validationId = Guid.NewGuid() });
        commit.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(commit)).Should().Be("Imports.InvalidState");

        await ImportValidationTestRunner.RunWorkerAsync(_factory.Services, id);

        var done = await GetJsonAsync(client, $"/api/v1/imports/batches/{id}/progress");
        done.GetProperty("status").GetString().Should().Be("Validated");
        done.GetProperty("phase").ValueKind.Should().Be(JsonValueKind.Null);
        done.GetProperty("leaseActive").GetBoolean().Should().BeFalse();
        var batch = await GetJsonAsync(client, $"/api/v1/imports/batches/{id}");
        Counts(batch, "customers").Should().Equal(4, 1, 2, 1, 2, 0, 1);
        (await client.GetAsync($"/api/v1/imports/batches/{id}/preview")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Progress_Should_Count_Rows_Written_By_The_Running_Validation()
    {
        var ctx = await SeedAsync();
        var cat = await SeedCatalogAsync(ctx.TenantId);
        var client = ClientFor(ctx.Token);
        var id = await UploadMainScenarioAsync(client, ctx, cat);
        await MapIdentityAsync(client, id);
        await ValidateAsync(client, id);

        // Yeniden doğrulama işçide (kira alınmış), önceki sonuçlar sıfırlanmış ve 6 satır yazılmış an.
        (await client.PostAsync($"/api/v1/imports/batches/{id}/validate", null)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await MutateBatchAsync(id, b => b.ClaimValidationLease("w-running", DateTime.UtcNow).IsSuccess.Should().BeTrue());
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pending = await db.ImportBatchRows.Where(r => r.BatchId == id).OrderBy(r => r.RowNumber).Skip(6)
                .Select(r => r.Id).ToListAsync();
            await db.ImportBatchRows.Where(r => pending.Contains(r.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.ValidationStatus, ImportRowValidationStatus.Pending));
        }

        var progress = await GetJsonAsync(client, $"/api/v1/imports/batches/{id}/progress");

        progress.GetProperty("phase").GetString().Should().Be("Validating");
        progress.GetProperty("leaseActive").GetBoolean().Should().BeTrue();
        progress.GetProperty("processed").GetInt32().Should().Be(6);
        progress.GetProperty("total").GetInt32().Should().Be(15);
        (await ImportValidationTestRunner.Processor(_factory.Services).ProcessBatchAsync(id, CancellationToken.None))
            .Should().BeFalse("kira başka işçide");
    }

    [Fact]
    public async Task Second_Validate_While_Queued_Or_Running_Should_Return_200_Without_New_Job()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var id = await UploadCustomersAsync(client);
        await MapCustomersAsync(client, id);

        (await client.PostAsync($"/api/v1/imports/batches/{id}/validate", null)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var replay = await client.PostAsync($"/api/v1/imports/batches/{id}/validate", null);

        replay.StatusCode.Should().Be(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());
        (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().Should().Be("Validating");

        // İşçi kirayı aldıktan sonra da aynı.
        await MutateBatchAsync(id, b => b.ClaimValidationLease("w-running", DateTime.UtcNow).IsSuccess.Should().BeTrue());
        (await client.PostAsync($"/api/v1/imports/batches/{id}/validate", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await StatusAsync(id)).Should().Be(ImportBatchStatus.Validating);
    }

    [Fact]
    public async Task Worker_Should_Take_Over_A_Crashed_Validation_Only_After_Lease_Expires()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx.Token);
        var id = await UploadCustomersAsync(client);
        await MapCustomersAsync(client, id);
        await ForceValidatingAsync(id, DateTime.UtcNow); // çalışan (kirası süren) işçi
        (await ImportValidationTestRunner.Processor(_factory.Services).ProcessBatchAsync(id, CancellationToken.None))
            .Should().BeFalse("kira dolmadan devralınmaz");

        // Çöken işçi: kirası 5 dk önce bitti.
        await MutateBatchAsync(id, b => b.ClaimValidationLease("w-crashed", DateTime.UtcNow.AddMinutes(-10)).IsSuccess.Should().BeTrue());

        await ImportValidationTestRunner.RunWorkerAsync(_factory.Services, id);

        var batch = await GetJsonAsync(client, $"/api/v1/imports/batches/{id}");
        batch.GetProperty("status").GetString().Should().Be("Validated");
        Counts(batch, "customers").Should().Equal(2, 2, 0, 0, 2, 0, 0);
    }

    // ================================================================ Yetki, kiracı yalıtımı

    [Fact]
    public async Task Other_Tenant_Should_Get_NotFound_On_All_B3_Endpoints()
    {
        var owner = await SeedAsync();
        var cat = await SeedCatalogAsync(owner.TenantId);
        var ownerClient = ClientFor(owner.Token);
        var id = await UploadMainScenarioAsync(ownerClient, owner, cat);
        await MapIdentityAsync(ownerClient, id);
        await ValidateAsync(ownerClient, id);
        var stranger = ClientFor((await SeedAsync()).Token);

        (await stranger.PostAsync($"/api/v1/imports/batches/{id}/validate", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        foreach (var path in new[] { "preview", "rows", "errors", "errors?format=csv" })
        {
            var response = await stranger.GetAsync($"/api/v1/imports/batches/{id}/{path}");
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, path);
            (await CodeAsync(response)).Should().Be("Imports.BatchNotFound");
        }

        (await StatusAsync(id)).Should().Be(ImportBatchStatus.Validated, "yabancı kiracı partiyi değiştiremez");
    }

    [Fact]
    public async Task Clinic_Bound_Batch_Should_Be_Hidden_From_User_Without_Clinic_Access()
    {
        var ctx = await SeedAsync();
        var otherClinic = await SeedClinicAsync(ctx.TenantId);
        var hidden = await SeedValidatedBatchForClinicAsync(ctx.TenantId, otherClinic);
        var client = ClientFor(ctx.Token);

        (await client.PostAsync($"/api/v1/imports/batches/{hidden}/validate", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/v1/imports/batches/{hidden}/preview")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/v1/imports/batches/{hidden}/rows")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/v1/imports/batches/{hidden}/errors")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Permissions_Should_Be_Enforced_Per_Endpoint()
    {
        var ctx = await SeedAsync();
        var cat = await SeedCatalogAsync(ctx.TenantId);
        var manager = ClientFor(ctx.Token);
        var id = await UploadMainScenarioAsync(manager, ctx, cat);
        await MapIdentityAsync(manager, id);
        await ValidateAsync(manager, id);

        var readOnly = ClientFor(await TokenAsync(ctx, new[] { "Imports.Read" }));
        (await readOnly.PostAsync($"/api/v1/imports/batches/{id}/validate", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await readOnly.GetAsync($"/api/v1/imports/batches/{id}/preview")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await readOnly.GetAsync($"/api/v1/imports/batches/{id}/rows")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await readOnly.GetAsync($"/api/v1/imports/batches/{id}/errors")).StatusCode.Should().Be(HttpStatusCode.OK);

        var none = ClientFor(await TokenAsync(ctx, new[] { "Clients.Read" }));
        (await none.PostAsync($"/api/v1/imports/batches/{id}/validate", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        foreach (var path in new[] { "preview", "rows", "errors" })
            (await none.GetAsync($"/api/v1/imports/batches/{id}/{path}")).StatusCode.Should().Be(HttpStatusCode.Forbidden, path);

        var anonymous = _factory.CreateClient();
        (await anonymous.PostAsync($"/api/v1/imports/batches/{id}/validate", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ================================================================ K9: temizlenmiş satır verisi

    [Fact]
    public async Task Purged_Batch_Should_Return_410_For_Rows_And_Errors_And_Counts_Only_For_Preview()
    {
        var ctx = await SeedAsync();
        var cat = await SeedCatalogAsync(ctx.TenantId);
        var client = ClientFor(ctx.Token);
        var id = await UploadMainScenarioAsync(client, ctx, cat);
        await MapIdentityAsync(client, id);
        await ValidateAsync(client, id);
        await PurgeAsync(id);

        foreach (var path in new[] { "rows", "errors" })
        {
            var response = await client.GetAsync($"/api/v1/imports/batches/{id}/{path}");
            response.StatusCode.Should().Be(HttpStatusCode.Gone, path);
            (await CodeAsync(response)).Should().Be("Imports.PayloadPurged");
        }

        var preview = await GetJsonAsync(client, $"/api/v1/imports/batches/{id}/preview");
        preview.GetProperty("payloadPurged").GetBoolean().Should().BeTrue();
        preview.GetProperty("canCommit").GetBoolean().Should().BeFalse();
        preview.GetProperty("unmappedValues").GetArrayLength().Should().Be(0);
        preview.GetProperty("summary").GetProperty("pets").GetProperty("total").GetInt32().Should().Be(5, "sayılar ve özet kalır");
    }

    // ================================================================ Yardımcılar

    private static int[] Counts(JsonElement batch, string table)
    {
        var c = batch.GetProperty("summary").GetProperty(table);
        return new[] { "total", "valid", "warning", "error", "willCreate", "willMatch", "willSkip" }
            .Select(k => c.GetProperty(k).GetInt32()).ToArray();
    }

    private static string Date(DateOnly d) => d.ToString("dd.MM.yyyy");

    /// <summary>
    /// 4 müşteri / 5 hasta / 6 aşı satırlı senaryo (beklenen sonuçlar test gövdelerindedir). Müşteri sayfasında
    /// Adres sütunu formül içeren bir hücre taşır (hata raporunda enjeksiyon denetimi).
    /// </summary>
    private async Task<Guid> UploadMainScenarioAsync(HttpClient client, SeedCtx ctx, CatalogIds cat)
    {
        var sfx = cat.Suffix;
        var today = OperationDayBounds.ToLocalDate(DateTime.UtcNow);
        var inWindow = Date(today.AddDays(2));
        var farFuture = Date(today.AddDays(30));

        var bytes = Workbook(
            ("Musteriler",
            [
                ["MusteriNo", "AdSoyad", "Telefon", "Eposta", "Adres"],
                ["M1", $"Ayşe {sfx}", "05321234567", "ayse@example.com", null],
                ["M2", $"Veli {sfx}", "02121234567", null, null], // sabit hat -> telefonsuz + uyarı
                ["M3", null, "05331234567", null, "=HYPERLINK(\"http://evil.example\",\"tıkla\")"], // ad yok -> hata
                ["M4", $"Ayşe {sfx}", "+90 532 123 45 67", null, null], // dosya içi mükerrer
            ]),
            ("Hayvanlar",
            [
                ["MusteriNo", "HayvanNo", "HayvanAdi", "Tur", "Mikrocip"],
                ["M1", "H1", "Pamuk", cat.SpeciesName, "985112345678901"],
                ["M2", "H2", "Karamel", cat.SpeciesName, null],
                ["M1", "H3", "Ejderi", "Ejderha", null], // eşlenmemiş tür
                ["M3", "H4", "Hatalı", cat.SpeciesName, null], // hatalı sahip
                ["M4", "H5", "Pamuk", cat.SpeciesName, null], // M4 -> M1'in sahibi; aynı hayvan mükerrer
            ]),
            ("Asilar",
            [
                ["HayvanNo", "AsiAdi", "UygulamaTarihi", "SonrakiAsiTarihi"],
                ["H1", cat.VaccineName, "10.04.2024", inWindow], // hatırlatma penceresinde
                ["H1", cat.VaccineName, "10.04.2023", "10.04.2024"], // geçmişte kalan sonraki aşı
                ["H2", cat.VaccineName, "10.04.2024", farFuture], // pencere dışı, sahibin e-postası yok
                ["H3", cat.VaccineName, "10.04.2024", null], // hatalı hayvan
                ["H1", cat.VaccineName, "10.04.2024", null], // aynı gün mükerrer aşı
                ["H1", "Bilinmeyen Aşı", "10.04.2024", null], // eşlenmemiş aşı
            ]));

        return await IdAsync(await PostAsync(client, Guid.NewGuid(), "Senaryo", ctx.ClinicId, ("file", "senaryo.xlsx", bytes, XlsxType)));
    }

    private static Dictionary<string, Dictionary<string, string>> IdentityColumns() => new()
    {
        ["customers"] = Map("MusteriNo", "MusteriNo", "AdSoyad", "AdSoyad", "Telefon", "Telefon", "Eposta", "Eposta"),
        ["pets"] = Map("MusteriNo", "MusteriNo", "HayvanNo", "HayvanNo", "HayvanAdi", "HayvanAdi", "Tur", "Tur", "Mikrocip", "Mikrocip"),
        ["vaccinations"] = Map("HayvanNo", "HayvanNo", "AsiAdi", "AsiAdi", "UygulamaTarihi", "UygulamaTarihi", "SonrakiAsiTarihi", "SonrakiAsiTarihi"),
    };

    private static Task MapIdentityAsync(HttpClient client, Guid id) => PutMappingAsync(client, id, IdentityColumns());

    private static async Task PutMappingAsync(
        HttpClient client, Guid id, Dictionary<string, Dictionary<string, string>> columns, object[]? valueMappings = null)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/v1/imports/batches/{id}/mapping", new { columns, valueMappings = valueMappings ?? Array.Empty<object>() });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static Dictionary<string, string> Map(params string[] pairs)
        => Enumerable.Range(0, pairs.Length / 2).ToDictionary(i => pairs[2 * i], i => pairs[2 * i + 1]);

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<List<JsonElement>> RowsAsync(HttpClient client, Guid id, string table)
        => (await GetJsonAsync(client, $"/api/v1/imports/batches/{id}/rows?table={table}&pageSize=200"))
            .GetProperty("items").EnumerateArray().ToList();

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

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, Guid requestId, string? name, Guid? clinicId,
        params (string Field, string FileName, byte[] Bytes, string ContentType)[] files)
    {
        var form = new MultipartFormDataContent { { new StringContent(requestId.ToString()), "requestId" } };
        if (name is not null)
            form.Add(new StringContent(name), "name");
        if (clinicId is { } c)
            form.Add(new StringContent(c.ToString()), "clinicId");
        foreach (var (field, fileName, bytes, contentType) in files)
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(content, field, fileName);
        }

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

    /// <summary>Tek müşteri sayfalı, her çağrıda benzersiz içerikli iki satırlık dosya.</summary>
    private async Task<Guid> UploadCustomersAsync(HttpClient client)
    {
        var bytes = Workbook(("Musteriler", [["AdSoyad", "Telefon"], [$"Ayşe {Guid.NewGuid():N}", "05321234567"], ["Veli Kaya", "05331234567"]]));
        return await IdAsync(await PostAsync(client, Guid.NewGuid(), null, null, ("file", "m.xlsx", bytes, XlsxType)));
    }

    private static Task MapCustomersAsync(HttpClient client, Guid id)
        => PutMappingAsync(client, id, new Dictionary<string, Dictionary<string, string>>
        {
            ["customers"] = Map("AdSoyad", "AdSoyad", "Telefon", "Telefon"),
        });

    private HttpClient ClientFor(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
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

    /// <summary>
    /// Partiyi verilen anda kuyruğa alınmış ve bir işçice (<c>w-crashed</c>) alınmış gösterir (çöken/süren iş taklidi);
    /// kira bu andan 5 dk sonra biter, yani geçmiş bir an süresi dolmuş kira üretir.
    /// </summary>
    private Task ForceValidatingAsync(Guid batchId, DateTime claimedAtUtc)
        => MutateBatchAsync(batchId, batch =>
        {
            var moved = batch.StartValidation(claimedAtUtc);
            moved.IsSuccess.Should().BeTrue(moved.Error.Message);
            batch.ClaimValidationLease("w-crashed", claimedAtUtc).IsSuccess.Should().BeTrue();
        });

    private async Task MutateBatchAsync(Guid batchId, Action<ImportBatch> mutate)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var batch = await db.ImportBatches.SingleAsync(b => b.Id == batchId);
        mutate(batch);
        await db.SaveChangesAsync();
    }

    private Task<JsonElement> ValidateAsync(HttpClient client, Guid id)
        => ImportValidationTestRunner.ValidateAsync(_factory.Services, client, id);

    private async Task PurgeAsync(Guid batchId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var batch = await db.ImportBatches.SingleAsync(b => b.Id == batchId);
        var rows = await db.ImportBatchRows.Where(r => r.BatchId == batchId).ToListAsync();

        var now = DateTime.UtcNow.AddDays(-40);
        batch.StartCommit(batch.ValidationId!.Value, now).IsSuccess.Should().BeTrue();
        batch.CompleteCommit(batch.CustomerCounts, batch.PetCounts, batch.VaccinationCounts, now).IsSuccess.Should().BeTrue();
        batch.PurgePayload(DateTime.UtcNow).IsSuccess.Should().BeTrue();
        foreach (var row in rows)
            row.PurgePayload();
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedValidatedBatchForClinicAsync(Guid tenantId, Guid clinicId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var batch = ImportBatch.Create(tenantId, clinicId, Guid.NewGuid(), "Gizli", Guid.NewGuid(), DateTime.UtcNow).Value!;
        batch.AttachFile(Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), 1, 0, 0);
        batch.ApplyMapping("{}", null, DateTime.UtcNow);
        batch.StartValidation(DateTime.UtcNow);
        batch.CompleteValidation(Guid.NewGuid(), batch.CustomerCounts, batch.PetCounts, batch.VaccinationCounts, DateTime.UtcNow);
        db.ImportBatches.Add(batch);
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

    private sealed record CatalogIds(string Suffix, Guid SpeciesId, string SpeciesName, Guid VaccineId, string VaccineName);

    /// <summary>Teste özgü tür ve kiracı aşı tanımı (genel katalogla çakışmasın diye benzersiz adlar).</summary>
    private async Task<CatalogIds> SeedCatalogAsync(Guid tenantId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var species = new Species($"IMV{suffix}", $"Imp Tür {suffix}");
        var vaccine = VaccineDefinition.CreateTenant(tenantId, $"IMPV{suffix}", $"Imp Aşı {suffix}");
        db.Species.Add(species);
        db.VaccineDefinitions.Add(vaccine);
        await db.SaveChangesAsync();
        return new CatalogIds(suffix, species.Id, species.Name, vaccine.Id, vaccine.Name);
    }

    private async Task<(Guid ClientId, Guid PetId)> SeedExistingAsync(
        Guid tenantId, CatalogIds cat, string fullName, string phone, string petName, string? chip)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var client = new Client(tenantId, fullName, phone, email: null, address: null);
        db.Clients.Add(client);
        var pet = new Pet(tenantId, client.Id, petName, cat.SpeciesId, microchipNumber: chip);
        db.Pets.Add(pet);
        await db.SaveChangesAsync();
        return (client.Id, pet.Id);
    }

    private async Task SeedVaccinationAsync(SeedCtx ctx, Guid petId, Guid vaccineId, DateOnly appliedOn)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var appliedAt = OperationDayBounds.NoonUtcForLocalDate(appliedOn);
        db.Vaccinations.Add(new Vaccination(
            ctx.TenantId, petId, ctx.ClinicId, null, vaccineId, "Mevcut aşı", VaccinationStatus.Applied, appliedAt, null, null));
        await db.SaveChangesAsync();
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

        var tenant = new Tenant($"imv-{Guid.NewGuid():N}"[..18]);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        var clinic = new Clinic(tenant.Id, $"Imp-{Guid.NewGuid():N}"[..14], "Istanbul");
        db.Clinics.Add(clinic);
        await db.SaveChangesAsync();

        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);
        var token = await IntegrationTestAuthHelper.SeedScopedClinicUserAndIssueTokenAsync(
            db, jwt, hasher, tenant.Id, clinic.Id, ManagePermissions);

        return new SeedCtx(tenant.Id, clinic.Id, token);
    }

    private sealed record SeedCtx(Guid TenantId, Guid ClinicId, string Token);
}
