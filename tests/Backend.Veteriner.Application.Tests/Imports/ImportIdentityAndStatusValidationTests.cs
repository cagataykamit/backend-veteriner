using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Domain.Imports;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Imports;

/// <summary>
/// E-vet yedeğiyle gelen kanonik alanlar (IMPORT_API_CONTRACT.md §13): fatura kimliği, müşteri/hasta durumu, alerji, türsüz
/// hasta bağlantısı ve yalnız ölçüm taşıyan klinik kayıt. Sentetik veri.
/// </summary>
public sealed class ImportIdentityAndStatusValidationTests
{
    // 2026-10-14 12:00 İstanbul.
    private static readonly DateTime Now = new(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);

    private static readonly Guid Cat = Guid.NewGuid();
    private static readonly Guid Dog = Guid.NewGuid();

    private static readonly ImportCatalogs Catalogs = new(
        Species: [new(Cat, "Kedi", "CAT", null), new(Dog, "Köpek", "DOG", null)], Breeds: [], Colors: [], Vaccines: []);

    private static readonly Dictionary<ImportTable, string[]> FieldsOf = new()
    {
        [ImportTable.Customers] = ["MusteriNo", "AdSoyad", "Telefon", "KimlikNo", "VergiDairesi", "Durum"],
        [ImportTable.Pets] = ["MusteriNo", "HayvanNo", "HayvanAdi", "Tur", "DogumTarihi", "Alerji", "Durum", "VefatTarihi"],
        [ImportTable.ClinicalRecords] = ["HayvanNo", "SahipAdSoyad", "HayvanAdi", "Tarih", "Kilo", "Ates"],
    };

    private static IReadOnlyList<ImportSourceRow> Table(ImportTable table, params string?[][] rows)
        => rows.Select((cells, i) => new ImportSourceRow(
                i + 2,
                FieldsOf[table].Select((h, c) => (h, v: c < cells.Length ? cells[c] : null)).ToDictionary(x => x.h, x => x.v)))
            .ToList();

    private static ImportValidationOutput Run(Dictionary<ImportTable, IReadOnlyList<ImportSourceRow>> tables)
    {
        var mapping = new ImportMappingDocument
        {
            Columns = tables.Keys.ToDictionary(ImportCanonicalNames.Key, t => FieldsOf[t].ToDictionary(f => f, f => f)),
        };
        return ImportValidationEngine.Run(new ImportValidationInput(
            tables, mapping, new ImportOptionsDocument(ImportOptionsDocument.Match), Catalogs, ImportExistingData.Empty, Now));
    }

    private static ImportRowResult Row(ImportValidationOutput output, ImportTable table, int row = 2)
        => output.Rows.Single(r => r.Table == table && r.RowNumber == row);

    private static ImportValidationOutput Customer(params string?[] cells)
        => Run(new() { [ImportTable.Customers] = Table(ImportTable.Customers, cells) });

    private static ImportValidationOutput OwnerAndPets(params string?[][] pets)
        => Run(new()
        {
            [ImportTable.Customers] = Table(ImportTable.Customers, ["M-1", "Ayşe Yılmaz", "05321234567"]),
            [ImportTable.Pets] = Table(ImportTable.Pets, pets),
        });

    // ================================================================ Fatura kimliği

    [Fact]
    public void Valid_National_Id_Should_Become_Individual_Identity()
    {
        var customer = (ImportNormalizedCustomer)Row(Customer("M-1", "Ayşe Yılmaz", null, "100 000 001 46"), ImportTable.Customers).Normalized;

        (customer.NationalId, customer.TaxNumber).Should().Be(("10000000146", null));
    }

    [Fact]
    public void Valid_Tax_Number_Should_Become_Corporate_Identity_With_Tax_Office()
    {
        var customer = (ImportNormalizedCustomer)Row(Customer("M-1", "Örnek Ltd.", null, "1234567890", "Kadıköy"), ImportTable.Customers).Normalized;

        (customer.NationalId, customer.TaxNumber, customer.TaxOffice).Should().Be((null, "1234567890", "Kadıköy"));
    }

    [Theory]
    [InlineData("12345678901")]
    [InlineData("12345")]
    [InlineData("abc")]
    public void Invalid_Identity_Should_Warn_And_Import_Without_It(string identity)
    {
        var row = Row(Customer("M-1", "Ayşe Yılmaz", null, identity), ImportTable.Customers);

        row.Status.Should().Be(ImportRowValidationStatus.Warning);
        row.Issues.Should().Contain(i => i.Code == ImportRules.TaxIdInvalid.Code && i.Value == identity);
        ((ImportNormalizedCustomer)row.Normalized).NationalId.Should().BeNull();
        row.Normalized.Action.Should().Be(ImportRowAction.Create);
    }

    // ================================================================ Durum

    [Fact]
    public void Passive_Customer_Should_Be_Imported_With_Reminders_Off()
    {
        var row = Row(Customer("M-1", "Ayşe Yılmaz", null, null, null, "Pasif"), ImportTable.Customers);

        row.Status.Should().Be(ImportRowValidationStatus.Valid);
        row.Issues.Should().ContainSingle(i => i.Code == ImportRules.InactiveClient.Code);
        ((ImportNormalizedCustomer)row.Normalized).RemindersOptedOut.Should().BeTrue();
    }

    [Fact]
    public void Unknown_Customer_Status_Should_Warn_And_Stay_Active()
    {
        var row = Row(Customer("M-1", "Ayşe Yılmaz", null, null, null, "Kara liste"), ImportTable.Customers);

        row.Issues.Should().Contain(i => i.Code == ImportRules.StatusUnrecognized.Code);
        ((ImportNormalizedCustomer)row.Normalized).RemindersOptedOut.Should().BeFalse();
    }

    [Theory]
    [InlineData("Vefat", "02.09.2025", true, "2025-09-02", null)]
    [InlineData("Ölü", null, true, null, null)]
    [InlineData("Ölü", "01.01.2027", true, null, "Imports.DeceasedDateIgnored")]
    [InlineData("Ölü", "01.01.2019", true, null, "Imports.DeceasedDateIgnored")]
    [InlineData("Aktif", null, false, null, null)]
    [InlineData("Kayıp", null, false, null, "Imports.StatusUnrecognized")]
    public void Pet_Status_Rules(string status, string? date, bool deceased, string? deceasedOn, string? code)
    {
        var row = Row(OwnerAndPets(["M-1", "H-1", "Pamuk", "Kedi", "01.01.2020", null, status, date]), ImportTable.Pets);

        var pet = (ImportNormalizedPet)row.Normalized;
        pet.Deceased.Should().Be(deceased);
        pet.DeceasedOn.Should().Be(deceasedOn is null ? null : DateOnly.Parse(deceasedOn));
        if (code is null)
            row.Status.Should().Be(ImportRowValidationStatus.Valid);
        else
            row.Issues.Should().Contain(i => i.Code == code);
    }

    // ================================================================ Alerji

    [Fact]
    public void Allergy_Should_Become_Alert_Note_And_Long_Text_Should_Also_Go_To_Notes()
    {
        var longText = new string('x', 250);
        var output = OwnerAndPets(["M-1", "H-1", "Pamuk", "Kedi", null, "Penisilin"], ["M-1", "H-2", "Duman", "Kedi", null, longText]);

        ((ImportNormalizedPet)Row(output, ImportTable.Pets).Normalized).AlertNote.Should().Be("Penisilin");
        var second = (ImportNormalizedPet)Row(output, ImportTable.Pets, 3).Normalized;
        second.AlertNote.Should().Be("Alerji ayrıntısı hasta notunda.");
        second.Notes.Should().Be($"Alerji: {longText}");
    }

    // ================================================================ Türsüz bağlantı ve klinik kayıt içeriği

    [Fact]
    public void Clinical_Record_Without_Species_Should_Link_To_The_Owners_Only_Pet_With_That_Name()
    {
        var output = Run(new()
        {
            [ImportTable.Customers] = Table(ImportTable.Customers, ["M-1", "Ayşe Yılmaz"]),
            [ImportTable.Pets] = Table(ImportTable.Pets, ["M-1", null, "Pamuk", "Kedi"]),
            [ImportTable.ClinicalRecords] = Table(ImportTable.ClinicalRecords, [null, "AYŞE YILMAZ", "pamuk", "01.06.2025", "4,2"]),
        });

        var row = Row(output, ImportTable.ClinicalRecords);
        row.Status.Should().Be(ImportRowValidationStatus.Valid, "yalnız kilo taşıyan kayıt boş sayılmaz");
        ((ImportNormalizedClinicalRecord)row.Normalized).PetKey.Should().Be("file:2");
    }

    [Fact]
    public void Clinical_Record_Without_Species_Should_Fail_When_Owner_Has_Two_Pets_With_That_Name()
    {
        var output = Run(new()
        {
            [ImportTable.Customers] = Table(ImportTable.Customers, ["M-1", "Ayşe Yılmaz"]),
            [ImportTable.Pets] = Table(ImportTable.Pets, ["M-1", null, "Pamuk", "Kedi"], ["M-1", null, "Pamuk", "Köpek"]),
            [ImportTable.ClinicalRecords] = Table(ImportTable.ClinicalRecords, [null, "Ayşe Yılmaz", "Pamuk", "01.06.2025", null, "38,5"]),
        });

        var row = Row(output, ImportTable.ClinicalRecords);
        row.Status.Should().Be(ImportRowValidationStatus.Error);
        row.Issues.Should().ContainSingle(i => i.Code == ImportRules.PetNotFound.Code)
            .Which.Message.Should().Contain("Tür olmadığı için");
    }

    [Theory]
    [InlineData("0", null)]
    [InlineData(null, null)]
    public void Clinical_Record_Without_Text_Or_Measurement_Should_Be_Empty(string? weight, string? temperature)
    {
        var output = Run(new()
        {
            [ImportTable.Customers] = Table(ImportTable.Customers, ["M-1", "Ayşe Yılmaz"]),
            [ImportTable.Pets] = Table(ImportTable.Pets, ["M-1", "H-1", "Pamuk", "Kedi"]),
            [ImportTable.ClinicalRecords] = Table(ImportTable.ClinicalRecords, ["H-1", null, null, "01.06.2025", weight, temperature]),
        });

        Row(output, ImportTable.ClinicalRecords).Issues.Should().Contain(i => i.Code == ImportRules.ClinicalRecordEmpty.Code);
    }
}
