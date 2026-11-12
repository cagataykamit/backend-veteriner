using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Products;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Imports;

/// <summary>
/// Tam geçiş tabloları (ADR-048, IMPORT_API_CONTRACT.md §12): açık bakiye, klinik geçmiş, hesap hareketi, randevu ve ürün
/// doğrulama kuralları. Girdi tamamen bellektedir; sentetik veri.
/// </summary>
public sealed class ImportFullMigrationValidationTests
{
    // 2026-10-14 12:00 İstanbul.
    private static readonly DateTime Now = new(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);

    private static readonly Guid Cat = Guid.NewGuid();

    private static readonly ImportCatalogs Catalogs = new(
        Species: [new(Cat, "Kedi", "CAT", null)], Breeds: [], Colors: [], Vaccines: []);

    private static readonly string[] CustomerFields = ["MusteriNo", "AdSoyad", "Telefon", "Eposta", "AcikBakiye", "BakiyeTarihi"];
    private static readonly string[] PetFields = ["MusteriNo", "HayvanNo", "HayvanAdi", "Tur", "DogumTarihi"];

    private static readonly string[] ClinicalFields =
        ["HayvanNo", "Tarih", "KayitTuru", "Sikayet", "Tani", "Tedavi", "Recete", "Not", "Hekim", "Kilo", "Ates"];

    private static readonly string[] AccountFields = ["MusteriNo", "Tarih", "IslemTuru", "Tutar", "Aciklama", "BelgeNo"];
    private static readonly string[] AppointmentFields = ["HayvanNo", "Tarih", "Saat", "Sure", "Hekim", "Neden", "RandevuTuru", "Not"];

    private static readonly string[] ProductFields =
        ["Ad", "UrunTuru", "Kategori", "Birim", "SatisFiyati", "Barkod", "StokKodu"];

    private static readonly Dictionary<ImportTable, string[]> FieldsOf = new()
    {
        [ImportTable.Customers] = CustomerFields,
        [ImportTable.Pets] = PetFields,
        [ImportTable.ClinicalRecords] = ClinicalFields,
        [ImportTable.AccountEntries] = AccountFields,
        [ImportTable.Appointments] = AppointmentFields,
        [ImportTable.Products] = ProductFields,
    };

    private static IReadOnlyList<ImportSourceRow> Table(ImportTable table, params string?[][] rows)
        => rows.Select((cells, i) => new ImportSourceRow(
                i + 2,
                FieldsOf[table].Select((h, c) => (h, v: c < cells.Length ? cells[c] : null)).ToDictionary(x => x.h, x => x.v)))
            .ToList();

    private static ImportValidationOutput Run(
        Dictionary<ImportTable, IReadOnlyList<ImportSourceRow>> tables, ImportExistingData? existing = null)
    {
        var mapping = new ImportMappingDocument
        {
            Columns = tables.Keys.ToDictionary(
                ImportCanonicalNames.Key, t => FieldsOf[t].ToDictionary(f => f, f => f)),
        };
        return ImportValidationEngine.Run(new ImportValidationInput(
            tables, mapping, new ImportOptionsDocument(ImportOptionsDocument.Match), Catalogs, existing ?? ImportExistingData.Empty, Now));
    }

    /// <summary>Bir müşteri (M-1) ve bir hasta (H-1, Pamuk) + verilen tablo.</summary>
    private static ImportValidationOutput WithOwnerAndPet(ImportTable table, string?[][] rows, ImportExistingData? existing = null)
        => Run(new()
        {
            [ImportTable.Customers] = Table(ImportTable.Customers, ["M-1", "Ayşe Yılmaz", "05321234567", "ayse@example.com"]),
            [ImportTable.Pets] = Table(ImportTable.Pets, ["M-1", "H-1", "Pamuk", "Kedi", "01.01.2020"]),
            [table] = Table(table, rows),
        }, existing);

    private static ImportRowResult Row(ImportValidationOutput output, ImportTable table, int row = 2)
        => output.Rows.Single(r => r.Table == table && r.RowNumber == row);

    private static string[] Codes(ImportRowResult r) => r.Issues.Select(i => i.Code).ToArray();

    // ================================================================ Açık bakiye (T3)

    [Fact]
    public void Positive_Balance_Should_Be_Kept_On_A_New_Customer_With_Its_Date()
    {
        var output = Run(new() { [ImportTable.Customers] = Table(ImportTable.Customers, [null, "Ayşe Yılmaz", "05321234567", null, "1.250,50", "31.08.2026"]) });

        var row = Row(output, ImportTable.Customers);
        row.Status.Should().Be(ImportRowValidationStatus.Valid);
        var customer = (ImportNormalizedCustomer)row.Normalized;
        customer.Action.Should().Be(ImportRowAction.Create);
        customer.OpeningBalance.Should().Be(1250.50m);
        customer.BalanceDate.Should().Be(new DateOnly(2026, 8, 31));
    }

    [Fact]
    public void Credit_Balance_Should_Be_Kept_On_A_New_Customer_Without_A_Warning()
    {
        var output = Run(new() { [ImportTable.Customers] = Table(ImportTable.Customers, [null, "Ayşe Yılmaz", "05321234567", null, "-300,25"]) });

        var row = Row(output, ImportTable.Customers);
        row.Status.Should().Be(ImportRowValidationStatus.Valid);
        ((ImportNormalizedCustomer)row.Normalized).OpeningBalance.Should().Be(-300.25m);
        row.Normalized.Action.Should().Be(ImportRowAction.Create);
    }

    [Theory]
    [InlineData("on bin", "Imports.AmountInvalid")]
    [InlineData("100 USD", "Imports.AmountInvalid")]
    public void Unreadable_Balance_Should_Warn_And_Import_The_Customer_Without_Balance(string balance, string code)
    {
        var output = Run(new() { [ImportTable.Customers] = Table(ImportTable.Customers, [null, "Ayşe Yılmaz", "05321234567", null, balance]) });

        var row = Row(output, ImportTable.Customers);
        row.Status.Should().Be(ImportRowValidationStatus.Warning);
        Codes(row).Should().Contain(code);
        ((ImportNormalizedCustomer)row.Normalized).OpeningBalance.Should().BeNull();
        row.Normalized.Action.Should().Be(ImportRowAction.Create);
    }

    [Fact]
    public void Balance_Should_Not_Be_Added_To_An_Existing_Customer()
    {
        var existing = new ImportExistingData([new(Guid.NewGuid(), "Ayşe Yılmaz", "905321234567", null)], [], []);

        var output = Run(
            new() { [ImportTable.Customers] = Table(ImportTable.Customers, [null, "Ayşe Yılmaz", "05321234567", null, "500"]) },
            existing);

        var row = Row(output, ImportTable.Customers);
        row.Normalized.Action.Should().Be(ImportRowAction.Match);
        Codes(row).Should().Contain(ImportRules.BalanceNotAppliedToExisting.Code);
        ((ImportNormalizedCustomer)row.Normalized).OpeningBalance.Should().BeNull();
    }

    [Fact]
    public void Future_Balance_Date_Should_Warn_And_Be_Ignored()
    {
        var output = Run(new() { [ImportTable.Customers] = Table(ImportTable.Customers, [null, "Ayşe Yılmaz", "05321234567", null, "500", "01.01.2027"]) });

        var row = Row(output, ImportTable.Customers);
        Codes(row).Should().Contain(ImportRules.BalanceDateIgnored.Code);
        ((ImportNormalizedCustomer)row.Normalized).BalanceDate.Should().BeNull();
        ((ImportNormalizedCustomer)row.Normalized).OpeningBalance.Should().Be(500m);
    }

    // ================================================================ Klinik geçmiş (T1)

    [Fact]
    public void Clinical_Record_Should_Link_To_The_File_Pet_And_Keep_Text_Fields()
    {
        var output = WithOwnerAndPet(ImportTable.ClinicalRecords,
            [["H-1", "12.05.2024", "Muayene", "İştahsızlık", "Gastroenterit", "Serum", "Amoksisilin 2x1", null, "Dr. Örnek", "4,5", "38,6"]]);

        var row = Row(output, ImportTable.ClinicalRecords);
        row.Status.Should().Be(ImportRowValidationStatus.Valid);
        var record = (ImportNormalizedClinicalRecord)row.Normalized;
        record.Action.Should().Be(ImportRowAction.Create);
        record.PetKey.Should().Be("file:2");
        record.OwnerKey.Should().Be("file:2");
        record.RecordedOn.Should().Be(new DateOnly(2024, 5, 12));
        record.Diagnosis.Should().Be("Gastroenterit");
        record.Prescription.Should().Be("Amoksisilin 2x1");
        record.WeightKg.Should().Be(4.5m);
        record.TemperatureC.Should().Be(38.6m);
    }

    [Fact]
    public void Clinical_Record_Without_Content_Should_Be_Skipped_Not_An_Error()
    {
        var output = WithOwnerAndPet(ImportTable.ClinicalRecords, [["H-1", "12.05.2024", "Muayene"]]);
        var row = Row(output, ImportTable.ClinicalRecords);

        Codes(row).Should().Contain(ImportRules.ClinicalRecordEmpty.Code);
        row.Status.Should().NotBe(ImportRowValidationStatus.Error);
        row.Normalized.Action.Should().Be(ImportRowAction.Skip);
    }

    [Fact]
    public void Empty_Clinical_Record_Should_Be_Skipped_Even_When_Pet_Is_Missing()
    {
        var output = WithOwnerAndPet(ImportTable.ClinicalRecords, [["H-404", "12.05.2024", "Muayene"]]);
        var row = Row(output, ImportTable.ClinicalRecords);

        Codes(row).Should().Contain(ImportRules.ClinicalRecordEmpty.Code);
        row.Normalized.Action.Should().Be(ImportRowAction.Skip);
    }

    [Theory]
    [InlineData("15.10.2026", "Imports.DateInFuture")]
    [InlineData("01.01.1980", "Imports.DateOutOfRange")]
    [InlineData(null, "Imports.RequiredMissing")]
    public void Clinical_Record_Date_Rules(string? date, string code)
    {
        var output = WithOwnerAndPet(ImportTable.ClinicalRecords, [["H-1", date, null, "Kusma"]]);

        var row = Row(output, ImportTable.ClinicalRecords);
        row.Status.Should().Be(ImportRowValidationStatus.Error);
        Codes(row).Should().Contain(code);
    }

    [Fact]
    public void Clinical_Record_Before_Birth_And_Bad_Temperature_Should_Warn()
    {
        var output = WithOwnerAndPet(ImportTable.ClinicalRecords, [["H-1", "01.06.2019", null, "Kusma", null, null, null, null, null, null, "60"]]);

        var row = Row(output, ImportTable.ClinicalRecords);
        row.Status.Should().Be(ImportRowValidationStatus.Warning);
        Codes(row).Should().Contain(ImportRules.RecordBeforeBirth.Code).And.Contain(ImportRules.TemperatureInvalid.Code);
        ((ImportNormalizedClinicalRecord)row.Normalized).TemperatureC.Should().BeNull();
    }

    [Fact]
    public void Same_Clinical_Record_Twice_In_File_Should_Skip_The_Second()
    {
        var output = WithOwnerAndPet(ImportTable.ClinicalRecords,
            [["H-1", "12.05.2024", null, "Kusma"], ["H-1", "12.05.2024", "Kontrol", "KUSMA "]]);

        Row(output, ImportTable.ClinicalRecords, 3).Normalized.Action.Should().Be(ImportRowAction.Skip);
        Codes(Row(output, ImportTable.ClinicalRecords, 3)).Should().Contain(ImportRules.DuplicateClinicalRecord.Code);
    }

    [Fact]
    public void Clinical_Record_Already_Imported_For_An_Existing_Pet_Should_Be_Skipped()
    {
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var existing = new ImportExistingData(
            [new(clientId, "Ayşe Yılmaz", "905321234567", null)],
            [new(petId, clientId, "Pamuk", Cat, null, null)],
            [])
        {
            ClinicalRecords = [new(petId, new DateOnly(2024, 5, 12), "Kusma", null, null, null, null, null, null, null)],
        };

        // Mevcut hastaya sahip + hasta adı + tür ile bağlanan satır (HayvanNo yok).
        string[] fields = ["SahipAdSoyad", "SahipTelefon", "HayvanAdi", "Tur", "Tarih", "Sikayet"];
        var rows = new List<ImportSourceRow>
        {
            new(2, fields.Zip(new string?[] { "Ayşe Yılmaz", "05321234567", "Pamuk", "Kedi", "12.05.2024", "KUSMA " })
                .ToDictionary(x => x.First, x => x.Second)),
        };
        var mapping = new ImportMappingDocument { Columns = new() { ["clinicalRecords"] = fields.ToDictionary(f => f, f => f) } };

        var output = ImportValidationEngine.Run(new ImportValidationInput(
            new Dictionary<ImportTable, IReadOnlyList<ImportSourceRow>> { [ImportTable.ClinicalRecords] = rows },
            mapping, new ImportOptionsDocument(ImportOptionsDocument.Match), Catalogs, existing, Now));

        var row = Row(output, ImportTable.ClinicalRecords);
        ((ImportNormalizedClinicalRecord)row.Normalized).PetKey.Should().Be($"db:{petId}");
        row.Normalized.Action.Should().Be(ImportRowAction.Skip);
        Codes(row).Should().Contain(ImportRules.DuplicateClinicalRecord.Code);
    }

    // ================================================================ Hesap hareketi (T4)

    [Theory]
    [InlineData("Tahsilat", ImportedAccountEntryKind.Credit)]
    [InlineData("ÖDEME", ImportedAccountEntryKind.Credit)]
    [InlineData("borç", ImportedAccountEntryKind.Debit)]
    [InlineData("Fatura", ImportedAccountEntryKind.Debit)]
    public void Account_Entry_Kind_Should_Be_Recognized(string kind, ImportedAccountEntryKind expected)
    {
        var output = WithOwnerAndPet(ImportTable.AccountEntries, [["M-1", "05.03.2025", kind, "350,00", "Aşı", "A-1"]]);

        var row = Row(output, ImportTable.AccountEntries);
        row.Status.Should().Be(ImportRowValidationStatus.Valid);
        var entry = (ImportNormalizedAccountEntry)row.Normalized;
        entry.Kind.Should().Be(expected);
        entry.Amount.Should().Be(350m);
        entry.OwnerKey.Should().Be("file:2");
        entry.Action.Should().Be(ImportRowAction.Create);
    }

    [Theory]
    [InlineData("İade", "350", "Imports.EntryKindUnrecognized")]
    [InlineData("Borç", "0", "Imports.EntryAmountInvalid")]
    [InlineData("Borç", "-5", "Imports.EntryAmountInvalid")]
    [InlineData("Borç", "abc", "Imports.EntryAmountInvalid")]
    public void Account_Entry_Invalid_Kind_Or_Amount_Should_Be_An_Error(string kind, string amount, string code)
    {
        var output = WithOwnerAndPet(ImportTable.AccountEntries, [["M-1", "05.03.2025", kind, amount]]);

        var row = Row(output, ImportTable.AccountEntries);
        row.Status.Should().Be(ImportRowValidationStatus.Error);
        Codes(row).Should().Contain(code);
    }

    [Fact]
    public void Account_Entry_For_Unknown_Customer_Should_Be_OwnerNotFound()
    {
        var output = WithOwnerAndPet(ImportTable.AccountEntries, [["M-9", "05.03.2025", "Borç", "10"]]);

        Codes(Row(output, ImportTable.AccountEntries)).Should().Contain(ImportRules.OwnerNotFound.Code);
    }

    [Fact]
    public void Same_Account_Entry_Already_Imported_Should_Be_Skipped()
    {
        var clientId = Guid.NewGuid();
        var existing = new ImportExistingData([new(clientId, "Ayşe Yılmaz", "905321234567", null)], [], [])
        {
            AccountEntries = [new(clientId, new DateOnly(2025, 3, 5), ImportedAccountEntryKind.Debit, 350m, "A-1", "Aşı")],
        };

        // Müşteri satırı mevcut müşteriyle eşleşir (db:), hareket ona bağlanır ve daha önce içe aktarılmış olarak görülür.
        var output = WithOwnerAndPet(ImportTable.AccountEntries, [["M-1", "05.03.2025", "Borç", "350", "aşı", "a-1"]], existing);

        var row = Row(output, ImportTable.AccountEntries);
        row.Normalized.Action.Should().Be(ImportRowAction.Skip);
        Codes(row).Should().Contain(ImportRules.DuplicateAccountEntry.Code);
    }

    // ================================================================ Randevu (T5)

    [Fact]
    public void Future_Appointment_Should_Be_Created_In_Istanbul_Time_With_Matched_Veterinarian()
    {
        var vet = Guid.NewGuid();
        var existing = ImportExistingData.Empty with { Veterinarians = [new(vet, "Dr. Örnek Hekim", null, null)] };

        var output = WithOwnerAndPet(ImportTable.Appointments,
            [["H-1", "20.12.2026", "14:30", "45", "DR. ÖRNEK HEKİM", "Aşı", "Kontrol", "Aç gelsin"]], existing);

        var row = Row(output, ImportTable.Appointments);
        row.Status.Should().Be(ImportRowValidationStatus.Valid);
        var appointment = (ImportNormalizedAppointment)row.Normalized;
        appointment.Action.Should().Be(ImportRowAction.Create);
        appointment.ScheduledAtUtc.Should().Be(new DateTime(2026, 12, 20, 11, 30, 0, DateTimeKind.Utc));
        appointment.DurationMinutes.Should().Be(45);
        appointment.AppointmentType.Should().Be(AppointmentType.Checkup);
        appointment.VeterinarianUserId.Should().Be(vet);
        appointment.ReasonId.Should().BeNull();
        appointment.Notes.Should().Be("Aç gelsin\nNeden: Aşı");
        appointment.OwnerHasEmail.Should().BeTrue();
        Codes(row).Should().Contain(ImportRules.ReasonFreeText.Code);
    }

    [Fact]
    public void Past_Appointment_Should_Be_Skipped_Not_Rejected()
    {
        var output = WithOwnerAndPet(ImportTable.Appointments, [["H-1", "01.10.2026 10:00"]]);

        var row = Row(output, ImportTable.Appointments);
        row.Status.Should().Be(ImportRowValidationStatus.Valid);
        row.Normalized.Action.Should().Be(ImportRowAction.Skip);
        Codes(row).Should().Contain(ImportRules.PastAppointmentSkipped.Code);
    }

    [Fact]
    public void Appointment_Without_Time_Should_Be_An_Error_And_Bad_Duration_Should_Default()
    {
        var output = WithOwnerAndPet(ImportTable.Appointments, [["H-1", "20.12.2026"], ["H-1", "21.12.2026", "09:00", "999"]]);

        Codes(Row(output, ImportTable.Appointments)).Should().Contain(ImportRules.TimeInvalid.Code);
        var second = Row(output, ImportTable.Appointments, 3);
        Codes(second).Should().Contain(ImportRules.DurationInvalid.Code);
        ((ImportNormalizedAppointment)second.Normalized).DurationMinutes.Should().Be(Appointment.DefaultDurationMinutes);
    }

    [Fact]
    public void Unknown_Veterinarian_Should_Go_To_Notes()
    {
        var output = WithOwnerAndPet(ImportTable.Appointments, [["H-1", "20.12.2026", "10:00", null, "Dr. Yok"]]);

        var row = Row(output, ImportTable.Appointments);
        Codes(row).Should().Contain(ImportRules.VeterinarianUnmatched.Code);
        ((ImportNormalizedAppointment)row.Normalized).Notes.Should().Be("Hekim: Dr. Yok");
    }

    [Fact]
    public void Same_Appointment_Twice_Should_Skip_The_Second()
    {
        var output = WithOwnerAndPet(ImportTable.Appointments, [["H-1", "20.12.2026", "10:00"], ["H-1", "2026-12-20T10:00:00"]]);

        Row(output, ImportTable.Appointments).Normalized.Action.Should().Be(ImportRowAction.Create);
        Codes(Row(output, ImportTable.Appointments, 3)).Should().Contain(ImportRules.DuplicateAppointment.Code);
    }

    // ================================================================ Ürün (T7)

    [Fact]
    public void Product_Should_Be_Created_With_Kind_Category_And_Price()
    {
        var category = Guid.NewGuid();
        var existing = ImportExistingData.Empty with { ProductCategories = [new(category, "Aşılar", null, null)] };

        var output = Run(new()
        {
            [ImportTable.Products] = Table(ImportTable.Products, ["Kuduz aşısı", "Aşı", "aşılar", null, "450,00", "8690000000001", "ASI-1"]),
        }, existing);

        var row = Row(output, ImportTable.Products);
        row.Status.Should().Be(ImportRowValidationStatus.Valid);
        var product = (ImportNormalizedProduct)row.Normalized;
        product.Action.Should().Be(ImportRowAction.Create);
        product.Kind.Should().Be(ProductKind.Vaccine);
        product.CategoryId.Should().Be(category);
        product.Unit.Should().Be("Adet");
        product.UnitPrice.Should().Be(450m);
    }

    [Fact]
    public void Product_Should_Match_Existing_By_Barcode_Then_By_Name_And_Kind()
    {
        var byBarcode = Guid.NewGuid();
        var byName = Guid.NewGuid();
        var existing = ImportExistingData.Empty with
        {
            Products = [new(byBarcode, "Eski ad", ProductKind.Medicine, "111"), new(byName, "Muayene ücreti", ProductKind.Service, null)],
        };

        var output = Run(new()
        {
            [ImportTable.Products] = Table(ImportTable.Products, ["Yeni ad", "İlaç", null, null, null, "111"], ["MUAYENE ÜCRETİ", "Hizmet"]),
        }, existing);

        Row(output, ImportTable.Products).Normalized.TargetId.Should().Be(byBarcode);
        Row(output, ImportTable.Products, 3).Normalized.TargetId.Should().Be(byName);
        Row(output, ImportTable.Products, 3).Normalized.Action.Should().Be(ImportRowAction.Match);
    }

    [Fact]
    public void Product_Duplicate_In_File_And_Unknown_Kind_And_Category()
    {
        var output = Run(new()
        {
            [ImportTable.Products] = Table(ImportTable.Products, ["Mama", "Yem", "Bilinmeyen"], ["mama", "Yem"]),
        });

        var first = Row(output, ImportTable.Products);
        Codes(first).Should().Contain(ImportRules.ProductKindDefaulted.Code).And.Contain(ImportRules.CategoryUnmapped.Code);
        ((ImportNormalizedProduct)first.Normalized).Kind.Should().Be(ProductKind.Other);
        Row(output, ImportTable.Products, 3).Normalized.MergedIntoRow.Should().Be(2);
        Codes(Row(output, ImportTable.Products, 3)).Should().Contain(ImportRules.DuplicateInFile.Code);
    }

    // ================================================================ Sayaçlar ve dosya düzeyi

    [Fact]
    public void Counts_Should_Hold_Invariants_For_New_Tables()
    {
        var output = WithOwnerAndPet(ImportTable.Appointments, [["H-1", "20.12.2026", "10:00"], ["H-1", "01.10.2026", "10:00"], ["H-9", "20.12.2026", "10:00"]]);

        var counts = output.CountsOf(ImportTable.Appointments);
        counts.Total.Should().Be(3);
        (counts.Valid + counts.Warning + counts.Error).Should().Be(counts.Total);
        (counts.WillCreate + counts.WillMatch + counts.WillSkip).Should().Be(counts.Valid + counts.Warning);
        counts.WillCreate.Should().Be(1);
        counts.WillSkip.Should().Be(1);
        counts.Error.Should().Be(1);
    }

    [Fact]
    public void Clinical_Records_Linked_Only_By_PetNo_Without_Pet_Table_Should_Block()
    {
        var mapping = new ImportMappingDocument
        {
            Columns = new() { ["clinicalRecords"] = new() { ["HayvanNo"] = "HayvanNo", ["Tarih"] = "Tarih" } },
        };

        ImportPreconditions.Check(mapping, [ImportTable.ClinicalRecords]).Should().Contain(ImportRules.PetLinkUnavailable);
    }

    [Fact]
    public void Account_Entries_Linked_Only_By_CustomerNo_Without_Customer_Table_Should_Block()
    {
        var mapping = new ImportMappingDocument
        {
            Columns = new() { ["accountEntries"] = new() { ["MusteriNo"] = "MusteriNo" } },
        };

        ImportPreconditions.Check(mapping, [ImportTable.AccountEntries]).Should().Contain(ImportRules.OwnerLinkUnavailable);
    }

    [Fact]
    public void Opening_Balance_Without_Clinic_Should_Block()
    {
        var mapping = new ImportMappingDocument
        {
            Columns = new() { ["customers"] = new() { ["AdSoyad"] = "AdSoyad", ["AcikBakiye"] = "Bakiye" } },
        };

        ImportPreconditions.Check(mapping, [ImportTable.Customers], hasClinic: false).Should().Contain(ImportRules.ClinicRequired);
        ImportPreconditions.Check(mapping, [ImportTable.Customers], hasClinic: true).Should().BeEmpty();
    }
}
