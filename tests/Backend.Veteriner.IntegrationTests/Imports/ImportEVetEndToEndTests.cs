using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Imports.Commit;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Imports;
using Backend.Veteriner.Infrastructure.Persistence;
using ClosedXML.Excel;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Imports;

/// <summary>
/// E-vet "Genel &gt; Yedek Al" yüklemesi uçtan uca (IMPORT_API_CONTRACT.md §13; gerçek SQL Server/LocalDB): dönüştürme → doğrula →
/// uygula → fatura kimliği, pasif müşteri, alerji, vefat (geçmiş kayıtlardan sonra), sonraki aşı tarihi, yalnız kilolu muayene, geçmiş görev →
/// geri al. Yapı gerçek dosyadan; değerler uydurmadır.
/// </summary>
[Collection("imports-api")]
public sealed class ImportEVetEndToEndTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] Permissions =
    [
        "Imports.Read", "Imports.Manage", "Imports.Commit", "Clients.Create", "Clients.Read", "Pets.Create", "Pets.Read",
        "Vaccinations.Create", "Examinations.Create", "Examinations.Read", "Charges.Create", "Charges.Read",
        "Appointments.Create", "Appointments.Read", "Products.Create", "Products.Read",
    ];

    private readonly CustomWebApplicationFactory _factory;

    public ImportEVetEndToEndTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task EVet_Backup_Should_Import_Identity_Status_History_And_Roll_Back()
    {
        var ctx = await SeedAsync();
        var http = ClientFor(ctx.Token);
        var owner = $"Ayşe {ctx.Suffix}";
        var company = $"Örnek Ltd {ctx.Suffix}";
        var today = OperationDayBounds.ToLocal(DateTime.UtcNow);
        var due = today.Date.AddDays(30).AddHours(10);
        var checkup = today.Date.AddDays(40).AddHours(14);

        var bytes = Workbook(
            ("Müşteri Listesi", [
                ["Kayıt Tarihi", "Müşteriler", "Bakiye", "Protokol No", "Kimlik No", "Gsm", "Vergi Dairesi", "Vergi No", "Durum", "Meslek", "Açıklama"],
                ["2024-01-01", owner, "1250.5000001", "101", "10000000146", "905321234567", null, null, "Aktif", "Öğretmen", "Akşam arayın"],
                ["2024-01-01", company, "0", "102", null, "905331234567", "Kadıköy", "1234567890", "Pasif"]]),
            ("Hasta Listesi", [
                ["Müşteriler", "Hastalar", "Protokol No", "Hasta Türü", "Cinsiyet", "Ağırlık", "Alerjik Durumu", "Ölüm Zamanı", "Durum"],
                [owner, "Pamuk", "201", ctx.SpeciesName, "Kısırlaştırılmış Dişi", "4.2", "Penisilin", null, "Aktif"],
                [owner, "Duman", "202", ctx.SpeciesName, "Erkek", null, null, "2025-09-02", "Ölü"]]),
            ("Randevu Listesi", [
                ["Müşteriler", "Hastalar", "Görev Tipi", "Başlangıç Tarihi", "Aşı Paketi", "Veteriner", "Durum"],
                [owner, "Duman", "Aşılama", "2025-01-10T10:00:00", ctx.VaccineName, "Dr. A", "Tamamlandı"],
                [owner, "Pamuk", "Tamamlandı", "2025-02-10T10:00:00", ctx.VaccineName, "Dr. A", "Tamamlandı"],
                [owner, "Pamuk", "Aşılama", due.ToString("yyyy-MM-ddTHH:mm:ss"), ctx.VaccineName, "Dr. A", "Yapılacak"],
                [owner, "Pamuk", "Kontrol Muayanesi", checkup.ToString("yyyy-MM-ddTHH:mm:ss"), null, "Dr. A", "Yapılacak"],
                [owner, "Duman", "Kısırlaştırma", "2025-03-01T09:00:00", null, "Dr. A", "Tamamlandı"],
                [owner, "Pamuk", "Kontrol Muayanesi", "2025-07-01T14:00:00", null, "Dr. A", "Yapılacak"],
                [null, null, "Maaş", "2025-03-01T09:00:00", null, "Dr. A", "Tamamlandı"]]),
            ("Muayene Listesi", [
                ["Tarih", "Hasta Sahibi", "Hasta Adı", "Veteriner Hekim", "Ağırlık", "Semptom"],
                ["2025-06-01T11:00:00", owner, "Pamuk", "Dr. A", "4.3", null],
                ["2025-06-02T11:00:00", owner, "Duman", "Dr. A", "0", "Kusma"],
                // Boş muayeneler: o gün başka kaydı yok → geliş; aynı gün dolu muayene var → atlanır.
                ["2025-08-01T10:00:00", owner, "Pamuk", "Dr. B", null, null],
                ["2025-06-01T15:00:00", owner, "Pamuk", "Dr. B", null, null]]));

        var productList =
            ";;;;;;;;14.10.2026;;\n"
            + "Ürün Listesi;;;;;;;;;;\n"
            + ";#;Ürün;Barkod-1;Barkod-2;Alış KDV;Alış Fiyatı;Satış KDV;Satış Fiyatı;Durum;\n"
            + "Aşı;;;;;;;;;;\n"
            + ";;;;;;;;;;\n"
            + $";1;Iev Kuduz {ctx.Suffix};;;10;₺100,00;10;₺250,00;;[+]\n"
            + "Hizmet;;;;;;;;;;\n"
            + ";;;;;;;;;;\n"
            + $";2;Iev Muayene {ctx.Suffix};;;0;₺0,00;20;₺500,00;;[+]\n";

        var id = await UploadAsync(http, ctx, bytes, productList);
        var validationId = (await ImportValidationTestRunner.ValidateAsync(_factory.Services, http, id)).GetProperty("validationId").GetGuid();
        var preview = await GetJsonAsync(http, $"/api/v1/imports/batches/{id}/preview");
        preview.GetProperty("canCommit").GetBoolean().Should().BeTrue(preview.ToString());

        var commit = await http.PostAsJsonAsync($"/api/v1/imports/batches/{id}/commit", new { validationId });
        commit.StatusCode.Should().Be(HttpStatusCode.Accepted, await commit.Content.ReadAsStringAsync());
        await RunCommitWorkerAsync();

        var batch = await GetJsonAsync(http, $"/api/v1/imports/batches/{id}");
        batch.GetProperty("status").GetString().Should().Be("Committed", batch.ToString());
        foreach (var (table, created) in new[] { ("customers", 2), ("pets", 2), ("vaccinations", 2), ("clinicalRecords", 5), ("appointments", 1) })
            batch.GetProperty("summary").GetProperty(table).GetProperty("created").GetInt32().Should().Be(created, table);

        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clients = await db.Clients.AsNoTracking().Where(c => c.TenantId == ctx.TenantId).ToListAsync();
            var person = clients.Single(c => c.FullName == owner);
            (person.PersonType, person.NationalId, person.RemindersOptedOut).Should().Be((ClientPersonType.Individual, "10000000146", false));
            var corporate = clients.Single(c => c.FullName == company);
            (corporate.PersonType, corporate.TaxNumber, corporate.TaxOffice, corporate.LegalName, corporate.RemindersOptedOut)
                .Should().Be((ClientPersonType.Corporate, "1234567890", "Kadıköy", company, true));
            person.Notes.Should().Be("Akşam arayın\nMeslek: Öğretmen\nEski programa kayıt: 01.01.2024");
            corporate.Notes.Should().Be("Eski programa kayıt: 01.01.2024");

            var products = await db.Products.AsNoTracking().Where(p => p.TenantId == ctx.TenantId).OrderBy(p => p.Name).ToListAsync();
            products.Select(p => (p.Name, p.VatRate, p.PurchasePrice, p.UnitPrice)).Should().Equal(
                ($"Iev Kuduz {ctx.Suffix}", 10m, 100m, 250m),
                ($"Iev Muayene {ctx.Suffix}", 20m, (decimal?)null, 500m));
            (await db.Charges.AsNoTracking().SingleAsync(c => c.ImportBatchId == id)).Amount.Should().Be(1250.50m);

            var pets = await db.Pets.AsNoTracking().Where(p => p.TenantId == ctx.TenantId).ToListAsync();
            var pamuk = pets.Single(p => p.Name == "Pamuk");
            (pamuk.Gender, pamuk.IsNeutered, pamuk.AlertFlags, pamuk.AlertNote, pamuk.Status).Should()
                .Be((PetGender.Female, true, PetAlertFlags.Allergy, "Penisilin", PetStatus.Active));
            var duman = pets.Single(p => p.Name == "Duman");
            (duman.Status, duman.DeceasedOn).Should().Be((PetStatus.Deceased, new DateOnly(2025, 9, 2)));

            var vaccinations = await db.Vaccinations.AsNoTracking().Where(v => v.ImportBatchId == id).ToListAsync();
            vaccinations.Should().Contain(v => v.PetId == duman.Id, "vefat geçmiş kayıtlardan sonra yazılır");
            OperationDayBounds.ToLocalDate(vaccinations.Single(v => v.PetId == pamuk.Id).DueAtUtc!.Value)
                .Should().Be(DateOnly.FromDateTime(due), "yapılacak aşı görevi sonraki aşı tarihidir");
            (await db.ImportedClinicalRecords.AsNoTracking().Where(r => r.ImportBatchId == id).Select(r => r.WeightKg).ToListAsync())
                .Should().Contain(4.3m, "yalnız kilo taşıyan muayene aktarılır");
            var tasks = await db.ImportedClinicalRecords.AsNoTracking()
                .Where(r => r.ImportBatchId == id && r.RecordType != "Muayene" && r.RecordType != ImportedClinicalRecord.ArrivalRecordType)
                .Select(r => new { r.PetId, r.RecordType, r.Notes, r.VeterinarianName })
                .ToListAsync();
            tasks.Should().BeEquivalentTo(new[]
            {
                new { PetId = duman.Id, RecordType = (string?)"Kısırlaştırma", Notes = (string?)"E-vet görevi: Tamamlandı", VeterinarianName = (string?)"Dr. A" },
                new { PetId = pamuk.Id, RecordType = (string?)"Kontrol Muayanesi", Notes = (string?)"E-vet görevi: Gelmedi", VeterinarianName = (string?)"Dr. A" },
            }, "geçmiş görevler eski program kaydı olur (ADR-048 eki)");
            (await db.ImportedClinicalRecords.AsNoTracking()
                    .Where(r => r.ImportBatchId == id && r.RecordType == ImportedClinicalRecord.ArrivalRecordType)
                    .Select(r => new { r.PetId, r.RecordedOn, r.VeterinarianName })
                    .ToListAsync())
                .Should().BeEquivalentTo(new[] { new { PetId = pamuk.Id, RecordedOn = new DateOnly(2025, 8, 1), VeterinarianName = (string?)"Dr. B" } },
                    "o gün başka kaydı olmayan boş muayene geliş olur; aynı gün muayenesi olan boş satır atlanır");
            (await db.Appointments.AsNoTracking().SingleAsync(a => a.ImportBatchId == id)).PetId.Should().Be(pamuk.Id);
        }

        var rollback = await http.PostAsJsonAsync($"/api/v1/imports/batches/{id}/rollback",
            new { requestId = Guid.NewGuid(), reason = "Deneme", blockedPolicy = "Fail" });
        rollback.StatusCode.Should().Be(HttpStatusCode.Accepted, await rollback.Content.ReadAsStringAsync());
        await RunRollbackWorkerAsync();

        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == id)).Status.Should().Be(ImportBatchStatus.RolledBack);
            (await db.Clients.CountAsync(c => c.TenantId == ctx.TenantId)).Should().Be(0);
            (await db.Pets.CountAsync(p => p.TenantId == ctx.TenantId)).Should().Be(0);
            (await db.Products.CountAsync(p => p.TenantId == ctx.TenantId)).Should().Be(0, "KDV'li ürün değişmiş sayılmaz, geri alınır");
            (await db.ImportedClinicalRecords.CountAsync(r => r.ImportBatchId == id)).Should().Be(0);
        }
    }

    /// <summary>
    /// Güncelleme aktarması (ADR-051, §14): klinik ilk yedekle geçer, E-vet'te çalışmayı sürdürür ve yeni bir tam yedek yükler. İkinci
    /// yedekte telefonu değişen, adı değişen (telefonsuz) müşteri ve adı değişen hasta kaynak numarasıyla eşleşir; yalnız yeni kayıtlar
    /// gelir; bakiye farkları önerilir ve seçimle açılır; ikinci partinin geri alınması eşleşen kayıtlara ve ilk partiye dokunmaz.
    /// </summary>
    [Fact]
    public async Task Second_EVet_Backup_Should_Add_Only_New_Records_And_Offer_Balance_Differences()
    {
        var ctx = await SeedAsync();
        var http = ClientFor(ctx.Token);
        var ayse = $"Ayşe {ctx.Suffix}";
        var mehmet = $"Mehmet {ctx.Suffix}";
        string[] customerHeader = ["Kayıt Tarihi", "Müşteriler", "Bakiye", "Protokol No", "Gsm", "Durum"];
        string[] petHeader = ["Müşteriler", "Hastalar", "Protokol No", "Hasta Türü", "Durum"];
        string[] taskHeader = ["Müşteriler", "Hastalar", "Görev Tipi", "Başlangıç Tarihi", "Aşı Paketi", "Veteriner", "Durum"];
        string[] examHeader = ["Tarih", "Hasta Sahibi", "Hasta Adı", "Veteriner Hekim", "Semptom"];

        var first = Workbook(
            ("Müşteri Listesi", [
                customerHeader,
                ["2024-01-01", ayse, "1250.50", "101", "905321234567", "Aktif"],
                ["2024-01-01", mehmet, "0", "102", null, "Aktif"]]),
            ("Hasta Listesi", [petHeader, [ayse, "Pamuk", "201", ctx.SpeciesName, "Aktif"], [mehmet, "Duman", "202", ctx.SpeciesName, "Aktif"]]),
            ("Randevu Listesi", [taskHeader, [ayse, "Pamuk", "Aşılama", "2025-01-10T10:00:00", ctx.VaccineName, "Dr. A", "Tamamlandı"]]),
            ("Muayene Listesi", [
                examHeader,
                ["2025-06-01T11:00:00", ayse, "Pamuk", "Dr. A", "Kusma"],
                ["2025-06-02T11:00:00", mehmet, "Duman", "Dr. A", "İshal"]]));

        var firstId = await UploadAsync(http, ctx, first);
        var firstValidation = (await ImportValidationTestRunner.ValidateAsync(_factory.Services, http, firstId)).GetProperty("validationId").GetGuid();
        (await GetJsonAsync(http, $"/api/v1/imports/batches/{firstId}/preview")).GetProperty("overlap").GetProperty("isUpdate").GetBoolean()
            .Should().BeFalse("ilk yükleme güncelleme değildir");
        (await http.PostAsJsonAsync($"/api/v1/imports/batches/{firstId}/commit", new { validationId = firstValidation })).StatusCode
            .Should().Be(HttpStatusCode.Accepted);
        await RunCommitWorkerAsync();

        // Bir hafta sonra: Ayşe'nin telefonu değişti, borcu arttı; Mehmet'in adı değişti (telefonu yok), borçlandı; Pamuk'un adı değişti;
        // yeni müşteri + hasta, yeni muayene ve aşı geldi. Eski kayıtlar yedekte aynen duruyor.
        var zeynep = $"Zeynep {ctx.Suffix}";
        var mehmetAli = $"Mehmet Ali {ctx.Suffix}";
        var second = Workbook(
            ("Müşteri Listesi", [
                customerHeader,
                ["2024-01-01", ayse, "1500", "101", "905339998877", "Aktif"],
                ["2024-01-01", mehmetAli, "100", "102", null, "Aktif"],
                ["2026-11-12", zeynep, "0", "103", "905351112233", "Aktif"]]),
            ("Hasta Listesi", [
                petHeader,
                [ayse, "Pamuk Hanım", "201", ctx.SpeciesName, "Aktif"],
                [mehmetAli, "Duman", "202", ctx.SpeciesName, "Aktif"],
                [zeynep, "Tarçın", "203", ctx.SpeciesName, "Aktif"]]),
            ("Randevu Listesi", [
                taskHeader,
                [ayse, "Pamuk Hanım", "Aşılama", "2025-01-10T10:00:00", ctx.VaccineName, "Dr. A", "Tamamlandı"],
                [ayse, "Pamuk Hanım", "Aşılama", "2026-11-14T10:00:00", ctx.VaccineName, "Dr. A", "Tamamlandı"]]),
            ("Muayene Listesi", [
                examHeader,
                ["2025-06-01T11:00:00", ayse, "Pamuk Hanım", "Dr. A", "Kusma"],
                ["2025-06-02T11:00:00", mehmetAli, "Duman", "Dr. A", "İshal"],
                ["2026-11-14T11:00:00", ayse, "Pamuk Hanım", "Dr. A", "Kontrol: iyileşmiş"],
                ["2026-11-13T09:00:00", zeynep, "Tarçın", "Dr. B", "Aşı öncesi muayene"]]));

        var secondId = await UploadAsync(http, ctx, second);
        var secondValidation = (await ImportValidationTestRunner.ValidateAsync(_factory.Services, http, secondId)).GetProperty("validationId").GetGuid();
        var preview = await GetJsonAsync(http, $"/api/v1/imports/batches/{secondId}/preview");
        preview.GetProperty("canCommit").GetBoolean().Should().BeTrue(preview.ToString());

        var overlap = preview.GetProperty("overlap");
        overlap.GetProperty("isUpdate").GetBoolean().Should().BeTrue(overlap.ToString());
        overlap.GetProperty("matchedBySourceId").GetInt32().Should().Be(4, "iki müşteri ve iki hasta kaynak numarasıyla eşleşir");
        overlap.GetProperty("tables").EnumerateArray()
            .Select(t => (t.GetProperty("table").GetString(), t.GetProperty("willCreate").GetInt32(), t.GetProperty("alreadyExists").GetInt32()))
            .Should().BeEquivalentTo(new (string?, int, int)[]
            {
                ("customers", 1, 2), ("pets", 1, 2), ("vaccinations", 1, 1), ("clinicalRecords", 2, 2),
            }, overlap.ToString());

        var differences = preview.GetProperty("balanceDifferences");
        differences.GetProperty("count").GetInt32().Should().Be(2, differences.ToString());
        differences.GetProperty("items").EnumerateArray()
            .Select(i => (i.GetProperty("currentBalance").GetDecimal(), i.GetProperty("fileBalance").GetDecimal(), i.GetProperty("difference").GetDecimal()))
            .Should().BeEquivalentTo(new[] { (1250.50m, 1500m, 249.50m), (0m, 100m, 100m) });

        var commit = await http.PostAsJsonAsync(
            $"/api/v1/imports/batches/{secondId}/commit", new { validationId = secondValidation, balanceDifferences = "Apply" });
        commit.StatusCode.Should().Be(HttpStatusCode.Accepted, await commit.Content.ReadAsStringAsync());
        await RunCommitWorkerAsync();

        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == secondId)).Status.Should().Be(ImportBatchStatus.Committed);
            var clients = await db.Clients.AsNoTracking().Where(c => c.TenantId == ctx.TenantId).ToListAsync();
            clients.Select(c => c.FullName).Should().BeEquivalentTo([ayse, mehmet, zeynep], "eşleşen müşterinin adı ezilmez, mükerrer müşteri yok");
            var pets = await db.Pets.AsNoTracking().Where(p => p.TenantId == ctx.TenantId).ToListAsync();
            pets.Select(p => p.Name).Should().BeEquivalentTo(["Pamuk", "Duman", "Tarçın"]);
            (await db.ImportedClinicalRecords.CountAsync(r => r.TenantId == ctx.TenantId)).Should().Be(4, "eski iki muayene ikinci kez yazılmaz");
            (await db.Vaccinations.CountAsync(v => v.TenantId == ctx.TenantId)).Should().Be(2);

            var ayseId = clients.Single(c => c.FullName == ayse).Id;
            var mehmetId = clients.Single(c => c.FullName == mehmet).Id;
            (await db.Charges.AsNoTracking().Where(c => c.ClientId == ayseId).Select(c => c.Amount).ToListAsync())
                .Should().BeEquivalentTo([1250.50m, 249.50m], "devir + seçimle açılan fark");
            (await db.Charges.AsNoTracking().Where(c => c.ClientId == mehmetId && c.ImportBatchId == secondId).Select(c => c.Description).SingleAsync())
                .Should().Be("Devir bakiyesi farkı (eski program)");
            (await db.ImportSourceLinks.AsNoTracking().Where(l => l.TenantId == ctx.TenantId).Select(l => new { l.Entity, l.SourceKey }).ToListAsync())
                .Should().BeEquivalentTo(new[]
                {
                    new { Entity = ImportSourceEntity.Client, SourceKey = "101" }, new { Entity = ImportSourceEntity.Client, SourceKey = "102" },
                    new { Entity = ImportSourceEntity.Client, SourceKey = "103" }, new { Entity = ImportSourceEntity.Pet, SourceKey = "201" },
                    new { Entity = ImportSourceEntity.Pet, SourceKey = "202" }, new { Entity = ImportSourceEntity.Pet, SourceKey = "203" },
                });
        }

        var rollbackPreview = await GetJsonAsync(http, $"/api/v1/imports/batches/{secondId}/rollback-preview");
        rollbackPreview.GetProperty("balanceAdjustments").GetProperty("delete").GetInt32().Should().Be(2, rollbackPreview.ToString());
        var rollback = await http.PostAsJsonAsync($"/api/v1/imports/batches/{secondId}/rollback",
            new { requestId = Guid.NewGuid(), reason = "Deneme", blockedPolicy = "Fail" });
        rollback.StatusCode.Should().Be(HttpStatusCode.Accepted, await rollback.Content.ReadAsStringAsync());
        await RunRollbackWorkerAsync();

        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == secondId)).Status.Should().Be(ImportBatchStatus.RolledBack);
            (await db.Clients.AsNoTracking().Where(c => c.TenantId == ctx.TenantId).Select(c => c.FullName).ToListAsync())
                .Should().BeEquivalentTo([ayse, mehmet], "eşleşen müşteri geri almada silinmez");
            (await db.Pets.AsNoTracking().Where(p => p.TenantId == ctx.TenantId).Select(p => p.Name).ToListAsync())
                .Should().BeEquivalentTo(["Pamuk", "Duman"]);
            (await db.ImportedClinicalRecords.CountAsync(r => r.TenantId == ctx.TenantId)).Should().Be(2, "ilk partinin kayıtları durur");
            (await db.Vaccinations.CountAsync(v => v.TenantId == ctx.TenantId)).Should().Be(1);
            (await db.Charges.AsNoTracking().Where(c => c.TenantId == ctx.TenantId).Select(c => c.Amount).ToListAsync())
                .Should().Equal([1250.50m], "fark hareketleri silinir, ilk devir durur");
        }
    }

    [Fact]
    public async Task Pet_Without_Species_Should_Take_Species_From_Its_Vaccines_Or_Keep_Them_Unverified()
    {
        var ctx = await SeedAsync();
        Guid dogVaccine, catVaccine, dog;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            dog = (await db.Species.AsNoTracking().SingleAsync(s => s.Code == "DOG")).Id;
            var cat = (await db.Species.AsNoTracking().SingleAsync(s => s.Code == "CAT")).Id;
            var dogDefinition = VaccineDefinition.CreateTenant(ctx.TenantId, $"IEVD{ctx.Suffix}", $"Iev Köpek Aşısı {ctx.Suffix}", DateTime.UtcNow, dog);
            var catDefinition = VaccineDefinition.CreateTenant(ctx.TenantId, $"IEVC{ctx.Suffix}", $"Iev Kedi Aşısı {ctx.Suffix}", DateTime.UtcNow, cat);
            db.VaccineDefinitions.AddRange(dogDefinition, catDefinition);
            await db.SaveChangesAsync();
            (dogVaccine, catVaccine) = (dogDefinition.Id, catDefinition.Id);
        }

        var http = ClientFor(ctx.Token);
        var owner = $"Ayşe {ctx.Suffix}";
        var bytes = Workbook(
            ("Müşteri Listesi", [["Kayıt Tarihi", "Müşteriler", "Protokol No", "Durum"], ["2018-01-01", owner, "101", "Aktif"]]),
            ("Hasta Listesi", [
                ["Müşteriler", "Hastalar", "Protokol No", "Hasta Türü", "Durum"],
                [owner, "Cesur", "126", "Diğer", "Aktif"],
                [owner, "Boncuk", "127", "Diğer", "Aktif"]]),
            ("Randevu Listesi", [
                ["Müşteriler", "Hastalar", "Görev Tipi", "Başlangıç Tarihi", "Aşı Paketi", "Veteriner", "Durum"],
                [owner, "Cesur", "Aşılama", "2021-08-03T10:00:00", $"Iev Köpek Aşısı {ctx.Suffix}", "Dr. A", "Tamamlandı"],
                [owner, "Boncuk", "Aşılama", "2021-08-03T10:00:00", $"Iev Köpek Aşısı {ctx.Suffix}", "Dr. A", "Tamamlandı"],
                [owner, "Boncuk", "Aşılama", "2021-09-03T10:00:00", $"Iev Kedi Aşısı {ctx.Suffix}", "Dr. A", "Tamamlandı"]]));

        var id = await UploadAsync(http, ctx, bytes);
        var validationId = (await ImportValidationTestRunner.ValidateAsync(_factory.Services, http, id)).GetProperty("validationId").GetGuid();
        var preview = await GetJsonAsync(http, $"/api/v1/imports/batches/{id}/preview");
        var counts = preview.GetProperty("issueCounts").EnumerateArray().ToDictionary(c => c.GetProperty("code").GetString()!, c => c.GetProperty("rowCount").GetInt32());
        (counts.GetValueOrDefault("Imports.SpeciesFromVaccines"), counts.GetValueOrDefault("Imports.VaccineSpeciesUnverified"),
                counts.GetValueOrDefault("VaccineDefinitions.InvalidSpecies"))
            .Should().Be((1, 2, 0), preview.ToString());

        var commit = await http.PostAsJsonAsync($"/api/v1/imports/batches/{id}/commit", new { validationId });
        commit.StatusCode.Should().Be(HttpStatusCode.Accepted, await commit.Content.ReadAsStringAsync());
        await RunCommitWorkerAsync();

        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pets = await db.Pets.AsNoTracking().Where(p => p.TenantId == ctx.TenantId).ToDictionaryAsync(p => p.Name);
            pets["Cesur"].SpeciesId.Should().Be(dog, "tek türe özgü aşısı köpek aşısı");
            (await db.Species.AsNoTracking().SingleAsync(s => s.Id == pets["Boncuk"].SpeciesId)).Code.Should().Be("OTHER");
            (await db.Vaccinations.AsNoTracking().Where(v => v.ImportBatchId == id).Select(v => new { v.PetId, v.VaccineDefinitionId }).ToListAsync())
                .Should().BeEquivalentTo(new[]
                {
                    new { PetId = pets["Cesur"].Id, VaccineDefinitionId = dogVaccine },
                    new { PetId = pets["Boncuk"].Id, VaccineDefinitionId = dogVaccine },
                    new { PetId = pets["Boncuk"].Id, VaccineDefinitionId = catVaccine },
                }, "türü bilinmeyen hastanın aşıları reddedilmez");
        }
    }

    [Fact]
    public async Task Ambiguous_Pet_Group_Should_Show_Its_Records_And_Bind_Records_Before_Birth_Automatically()
    {
        var ctx = await SeedAsync();
        var http = ClientFor(ctx.Token);
        var owner = $"Ayşe {ctx.Suffix}";
        var bytes = Workbook(
            ("Müşteri Listesi", [["Kayıt Tarihi", "Müşteriler", "Protokol No", "Durum"], ["2018-01-01", owner, "101", "Aktif"]]),
            ("Hasta Listesi", [
                ["Müşteriler", "Hastalar", "Protokol No", "Hasta Türü", "Doğum Tarihi", "Durum"],
                [owner, "Boncuk", "301", ctx.SpeciesName, "2019-01-01", "Aktif"],
                [owner, "Boncuk", "302", ctx.SpeciesName, "2024-01-01", "Aktif"]]),
            ("Muayene Listesi", [
                ["Tarih", "Hasta Sahibi", "Hasta Adı", "Veteriner Hekim", "Semptom"],
                ["2023-05-01T11:00:00", owner, "Boncuk", "Dr. A", "Topallık"],
                ["2025-06-01T11:00:00", owner, "Boncuk", "Dr. A", "Kusma ve ishal"]]));

        var id = await UploadAsync(http, ctx, bytes);
        await ImportValidationTestRunner.ValidateAsync(_factory.Services, http, id);
        var preview = await GetJsonAsync(http, $"/api/v1/imports/batches/{id}/preview");

        const string group = "Belirsiz: 301 / 302";
        preview.GetProperty("unmappedValues").EnumerateArray()
            .Where(u => u.GetProperty("kind").GetString() == "pet")
            .Select(u => (u.GetProperty("source").GetString(), u.GetProperty("rowCount").GetInt32()))
            .Should().Equal((group, 1));
        var row = preview.GetProperty("petRows").EnumerateArray().Should().ContainSingle().Subject;
        (row.GetProperty("reference").GetString(), row.GetProperty("table").GetString(), row.GetProperty("date").GetString(),
                row.GetProperty("title").GetString(), row.GetProperty("summary").GetString())
            .Should().Be((group, "clinicalRecords", "2025-06-01", "Muayene", "Kusma ve ishal"));
    }

    [Fact]
    public async Task EVet_Reports_Should_Be_Rejected_Without_EVet_Profile()
    {
        var ctx = await SeedAsync();
        var http = ClientFor(ctx.Token);
        var form = new MultipartFormDataContent
        {
            { new StringContent(Guid.NewGuid().ToString()), "requestId" },
            { new StringContent(ctx.ClinicId.ToString()), "clinicId" },
        };
        form.Add(Xlsx(Workbook(("Müşteri Listesi", [["Müşteriler"], ["Ayşe"]]))), "file", "yedek.xlsx");
        form.Add(new ByteArrayContent("a;b\n"u8.ToArray()) { Headers = { ContentType = new MediaTypeHeaderValue("text/csv") } }, "evetPatientReportFile", "hasta.csv");

        var response = await http.PostAsync("/api/v1/imports/batches", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Imports.UnsupportedFile");
    }

    // ================================================================ Yardımcılar

    private static IOptions<ImportCommitOptions> WorkerOptions()
        => Options.Create(new ImportCommitOptions { Backpressure = new ImportBackpressureOptions { Enabled = false } });

    private async Task RunCommitWorkerAsync()
    {
        var processor = new ImportCommitProcessor(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(), WorkerOptions(), TimeProvider.System,
            NullLogger<ImportCommitProcessor>.Instance, $"w-ev-{Guid.NewGuid():N}"[..20]);
        for (var i = 0; i < 20 && await processor.ProcessNextAsync(CancellationToken.None); i++)
        {
        }
    }

    private async Task RunRollbackWorkerAsync()
    {
        var processor = new ImportRollbackProcessor(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(), WorkerOptions(), TimeProvider.System,
            NullLogger<ImportRollbackProcessor>.Instance, $"w-er-{Guid.NewGuid():N}"[..20]);
        for (var i = 0; i < 20 && await processor.ProcessNextAsync(CancellationToken.None); i++)
        {
        }
    }

    private static ByteArrayContent Xlsx(byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        return content;
    }

    private static async Task<Guid> UploadAsync(HttpClient http, SeedCtx ctx, byte[] bytes, string? productList = null)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(Guid.NewGuid().ToString()), "requestId" },
            { new StringContent(ctx.ClinicId.ToString()), "clinicId" },
            { new StringContent("EVet"), "sourceProfile" },
        };
        form.Add(Xlsx(bytes), "file", "backup.xlsx");
        if (productList is not null)
        {
            var csv = new ByteArrayContent([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(productList)]);
            csv.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
            form.Add(csv, "productsFile", "urunler.csv");
        }

        var response = await http.PostAsync("/api/v1/imports/batches", form);
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        var batch = await response.Content.ReadFromJsonAsync<JsonElement>();
        var notes = batch.GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString()).ToList();
        notes.Should().Contain(c => c!.StartsWith("Imports.EVet."));
        var id = batch.GetProperty("id").GetGuid();

        // Notlar partiye yazılır: sayfa yenilenince ya da parti geçmişten açılınca da ayrıntıyla döner.
        var detail = await http.GetFromJsonAsync<JsonElement>($"/api/v1/imports/batches/{id}");
        var stored = detail.GetProperty("warnings").EnumerateArray().ToList();
        stored.Select(w => w.GetProperty("code").GetString()).Should().Equal(notes);
        stored.Where(w => w.GetProperty("code").GetString()!.StartsWith("Imports.EVet."))
            .Should().OnlyContain(w => w.GetProperty("severity").ValueKind == JsonValueKind.String
                                       && w.GetProperty("count").ValueKind == JsonValueKind.Number);

        // Dönüştürülmüş başlıklar kanonik: önerilen eşleme kaydedilir.
        var columns = batch.GetProperty("tables").EnumerateArray().ToDictionary(
            t => t.GetProperty("table").GetString()!,
            t => t.GetProperty("columns").EnumerateArray()
                .Where(c => c.GetProperty("suggested").ValueKind == JsonValueKind.String)
                .ToDictionary(c => c.GetProperty("suggested").GetString()!, c => c.GetProperty("source").GetString()!));
        var mapped = await http.PutAsJsonAsync($"/api/v1/imports/batches/{id}/mapping", new { columns, valueMappings = Array.Empty<object>() });
        mapped.StatusCode.Should().Be(HttpStatusCode.OK, await mapped.Content.ReadAsStringAsync());
        return id;
    }

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

    private static async Task<JsonElement> GetJsonAsync(HttpClient http, string url)
    {
        var response = await http.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"{url}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private HttpClient ClientFor(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Kiracı + klinik + teste özgü tür ve aşı tanımı + yetkili kullanıcı.</summary>
    private async Task<SeedCtx> SeedAsync()
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var tenant = new Tenant($"iev-{Guid.NewGuid():N}"[..18], DateTime.UtcNow);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        var clinic = new Clinic(tenant.Id, $"Iev-{Guid.NewGuid():N}"[..14], "Istanbul");
        db.Clinics.Add(clinic);
        var species = new Species($"IEV{suffix}", $"Iev Tür {suffix}");
        db.Species.Add(species);
        var vaccine = VaccineDefinition.CreateTenant(tenant.Id, $"IEVV{suffix}", $"Iev Aşı {suffix}", DateTime.UtcNow);
        db.VaccineDefinitions.Add(vaccine);
        await db.SaveChangesAsync();

        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);
        var token = await IntegrationTestAuthHelper.SeedScopedClinicUserAndIssueTokenAsync(db, jwt, hasher, tenant.Id, clinic.Id, Permissions);
        return new SeedCtx(tenant.Id, clinic.Id, token, suffix, species.Name, vaccine.Name);
    }

    private sealed record SeedCtx(Guid TenantId, Guid ClinicId, string Token, string Suffix, string SpeciesName, string VaccineName);
}
