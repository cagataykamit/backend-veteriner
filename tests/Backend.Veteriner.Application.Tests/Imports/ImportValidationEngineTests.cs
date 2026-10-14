using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Imports;

/// <summary>
/// Kuru çalıştırma motoru (IMPORT_API_CONTRACT.md §6.4): satır kuralları, mükerrer, çip çakışması, bağlantılar ve
/// aşı tarih kuralları. Girdi tamamen bellektedir; sentetik veri.
/// </summary>
public sealed class ImportValidationEngineTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);

    private static readonly Guid Dog = Guid.NewGuid();
    private static readonly Guid Cat = Guid.NewGuid();
    private static readonly Guid Tekir = Guid.NewGuid();
    private static readonly Guid Rabies = Guid.NewGuid();
    private static readonly Guid FeLv = Guid.NewGuid();
    private static readonly Guid Bronchine = Guid.NewGuid();

    private static readonly ImportCatalogs Catalogs = new(
        Species: [new(Dog, "Köpek", "DOG", null), new(Cat, "Kedi", "CAT", null)],
        Breeds: [new(Tekir, "Tekir", null, Cat)],
        Colors: [],
        Vaccines:
        [
            new(Rabies, "Kuduz", "RABIES", null),
            new(FeLv, "Lösemi", "LEUKEMIA", Cat),
            new(Bronchine, "Bronşin", "BRONCHINE", Dog),
        ]);

    // Eşleme: kanonik alan → aynı adlı kaynak sütun.
    private static readonly string[] CustomerFields = ["MusteriNo", "AdSoyad", "Telefon", "Eposta", "Adres"];

    private static readonly string[] PetFields =
    [
        "MusteriNo", "SahipAdSoyad", "SahipTelefon", "HayvanNo", "HayvanAdi", "Tur", "Irk", "Cinsiyet", "DogumTarihi",
        "Kisir", "Mikrocip", "Renk", "Agirlik", "KarneNo", "Not",
    ];

    private static readonly string[] VaccinationFields =
    [
        "HayvanNo", "SahipAdSoyad", "SahipTelefon", "HayvanAdi", "Tur", "AsiAdi", "UygulamaTarihi", "SonrakiAsiTarihi",
        "Seri", "Uygulayan", "Not",
    ];

    private static Dictionary<string, string> Identity(string[] fields) => fields.ToDictionary(f => f, f => f);

    private static ImportMappingDocument Mapping(
        string[]? customers = null,
        string[]? pets = null,
        string[]? vaccinations = null,
        params ImportValueMapping[] valueMappings)
    {
        var columns = new Dictionary<string, Dictionary<string, string>>();
        if (customers is not null) columns["customers"] = Identity(customers);
        if (pets is not null) columns["pets"] = Identity(pets);
        if (vaccinations is not null) columns["vaccinations"] = Identity(vaccinations);
        return new ImportMappingDocument { Columns = columns, ValueMappings = [.. valueMappings] };
    }

    /// <summary>Başlık dizisi + satırlar (satır numarası 2'den başlar).</summary>
    private static IReadOnlyList<ImportSourceRow> Table(string[] headers, params string?[][] rows)
        => rows.Select((cells, i) => new ImportSourceRow(
                i + 2,
                headers.Select((h, c) => (h, v: c < cells.Length ? cells[c] : null)).ToDictionary(x => x.h, x => x.v)))
            .ToList();

    private static ImportValidationOutput Run(
        IReadOnlyList<ImportSourceRow>? customers = null,
        IReadOnlyList<ImportSourceRow>? pets = null,
        IReadOnlyList<ImportSourceRow>? vaccinations = null,
        ImportMappingDocument? mapping = null,
        ImportExistingData? existing = null,
        string onDuplicateClient = ImportOptionsDocument.Match)
    {
        var tables = new Dictionary<ImportTable, IReadOnlyList<ImportSourceRow>>();
        if (customers is not null) tables[ImportTable.Customers] = customers;
        if (pets is not null) tables[ImportTable.Pets] = pets;
        if (vaccinations is not null) tables[ImportTable.Vaccinations] = vaccinations;

        return ImportValidationEngine.Run(new ImportValidationInput(
            tables,
            mapping ?? Mapping(
                customers is null ? null : CustomerFields,
                pets is null ? null : PetFields,
                vaccinations is null ? null : VaccinationFields),
            new ImportOptionsDocument(onDuplicateClient),
            Catalogs,
            existing ?? ImportExistingData.Empty,
            Now));
    }

    // Satır kısayolları -----------------------------------------------------

    private static string?[] Customer(
        string? no = null, string? name = "Ayşe Yılmaz", string? phone = "05321234567", string? email = null, string? address = null)
        => [no, name, phone, email, address];

    private static string?[] Pet(
        string? customerNo = null, string? owner = null, string? ownerPhone = null, string? petNo = null,
        string? name = "Pamuk", string? species = "Kedi", string? breed = null, string? gender = null,
        string? birth = null, string? neutered = null, string? chip = null, string? color = null,
        string? weight = null, string? passport = null, string? note = null)
        => [customerNo, owner, ownerPhone, petNo, name, species, breed, gender, birth, neutered, chip, color, weight, passport, note];

    private static string?[] Vaccination(
        string? petNo = null, string? owner = null, string? ownerPhone = null, string? petName = null, string? species = null,
        string? vaccine = "Kuduz", string? applied = "10.04.2024", string? due = null,
        string? serial = null, string? by = null, string? note = null)
        => [petNo, owner, ownerPhone, petName, species, vaccine, applied, due, serial, by, note];

    private static IReadOnlyList<ImportSourceRow> Customers(params string?[][] rows) => Table(CustomerFields, rows);
    private static IReadOnlyList<ImportSourceRow> Pets(params string?[][] rows) => Table(PetFields, rows);
    private static IReadOnlyList<ImportSourceRow> Vaccinations(params string?[][] rows) => Table(VaccinationFields, rows);

    private static ImportRowResult Single(ImportValidationOutput output, ImportTable table, int row = 2)
        => output.Rows.Single(r => r.Table == table && r.RowNumber == row);

    private static string[] Codes(ImportRowResult r) => r.Issues.Select(i => i.Code).ToArray();

    // ================================================================ Müşteri

    [Fact]
    public void Customer_Valid_Row_Should_Be_Created_With_Normalized_Values()
    {
        var output = Run(Customers(Customer(no: "M-1", name: "  Ayşe   Yılmaz ", phone: "0532 123 45 67", email: "Ayse@Example.COM", address: "Örnek Mah.")));

        var r = Single(output, ImportTable.Customers);
        r.Status.Should().Be(ImportRowValidationStatus.Valid);
        r.Issues.Should().BeEmpty();
        var n = r.Normalized.Should().BeOfType<ImportNormalizedCustomer>().Subject;
        n.Action.Should().Be(ImportRowAction.Create);
        n.Reference.Should().Be("M-1");
        n.FullName.Should().Be("Ayşe Yılmaz");
        n.Phone.Should().Be("905321234567");
        n.Email.Should().Be("ayse@example.com");
        n.Address.Should().Be("Örnek Mah.");
    }

    [Fact]
    public void Customer_Missing_Name_Should_Be_Error_With_No_Action()
    {
        var r = Single(Run(Customers(Customer(name: null))), ImportTable.Customers);

        r.Status.Should().Be(ImportRowValidationStatus.Error);
        Codes(r).Should().Equal("Imports.RequiredMissing");
        r.Normalized.Action.Should().BeNull();
    }

    [Theory]
    [InlineData("A", "Imports.NameTooShort")]
    [InlineData("  B  ", "Imports.NameTooShort")]
    public void Customer_Short_Name_Should_Be_Error(string name, string code)
    {
        var r = Single(Run(Customers(Customer(name: name))), ImportTable.Customers);

        r.Status.Should().Be(ImportRowValidationStatus.Error);
        Codes(r).Should().Contain(code);
    }

    [Fact]
    public void Customer_Long_Name_And_Address_Should_Be_Field_Too_Long()
    {
        var r = Single(
            Run(Customers(Customer(name: new string('a', 301), address: new string('b', 501)))),
            ImportTable.Customers);

        r.Status.Should().Be(ImportRowValidationStatus.Error);
        r.Issues.Where(i => i.Code == "Imports.FieldTooLong").Select(i => i.Field).Should().BeEquivalentTo("AdSoyad", "Adres");
    }

    [Theory]
    [InlineData("02121234567")]
    [InlineData("0 212 123 45 67")]
    [InlineData("abc")]
    public void Customer_Landline_Or_Invalid_Phone_Should_Import_Without_Phone_And_Warn_K5(string phone)
    {
        var r = Single(Run(Customers(Customer(phone: phone))), ImportTable.Customers);

        r.Status.Should().Be(ImportRowValidationStatus.Warning);
        var issue = r.Issues.Single(i => i.Code == "Imports.PhoneInvalid");
        issue.Severity.Should().Be(ImportIssueSeverity.Warning);
        issue.Value.Should().Be(phone, "özgün numara raporda görünür");
        var n = (ImportNormalizedCustomer)r.Normalized;
        n.Phone.Should().BeNull();
        n.Action.Should().Be(ImportRowAction.Create);
    }

    [Fact]
    public void Customer_Invalid_Email_Should_Warn_And_Drop_Email()
    {
        var r = Single(Run(Customers(Customer(email: "ayse@"))), ImportTable.Customers);

        r.Status.Should().Be(ImportRowValidationStatus.Warning);
        Codes(r).Should().Equal("Imports.EmailInvalid");
        ((ImportNormalizedCustomer)r.Normalized).Email.Should().BeNull();
    }

    [Fact]
    public void Customer_Duplicate_Reference_Should_Error_On_Second_Row()
    {
        var output = Run(Customers(
            Customer(no: "M-1", name: "Ayşe Yılmaz"),
            Customer(no: "m-1", name: "Veli Kaya", phone: "05331234567")));

        Single(output, ImportTable.Customers, 2).Status.Should().Be(ImportRowValidationStatus.Valid);
        var second = Single(output, ImportTable.Customers, 3);
        second.Status.Should().Be(ImportRowValidationStatus.Error);
        Codes(second).Should().Contain("Imports.DuplicateReference");
    }

    [Fact]
    public void Customer_Duplicate_Name_And_Phone_In_File_Should_Merge_Into_First_Row()
    {
        var output = Run(Customers(
            Customer(name: "Ayşe Yılmaz", phone: "05321234567"),
            Customer(name: "ayşe yılmaz", phone: "+90 532 123 45 67")));

        var second = Single(output, ImportTable.Customers, 3);
        second.Status.Should().Be(ImportRowValidationStatus.Warning);
        Codes(second).Should().Equal("Imports.DuplicateInFile");
        second.Normalized.Action.Should().Be(ImportRowAction.Skip);
        second.Normalized.MergedIntoRow.Should().Be(2);
        output.CountsOf(ImportTable.Customers).Should().Match<ImportTableCounts>(c =>
            c.WillCreate == 1 && c.WillSkip == 1 && c.Warning == 1 && c.Valid == 1);
    }

    [Fact]
    public void Customer_Duplicate_Key_Is_Turkish_Aware_So_Upper_Case_And_Dotted_Dotless_I_Merge()
    {
        // ADR-019 K4: "AYŞE YILMAZ" / "Ayşe Yılmaz" ve "ISPARTA" / "Isparta" aynı kişi (eski ToLowerInvariant sınırı kalktı).
        var output = Run(Customers(
            Customer(name: "Ayşe Yılmaz", phone: "05321234567"),
            Customer(name: "AYŞE YILMAZ", phone: "05321234567"),
            Customer(name: "Isparta Şahin", phone: "05329998877"),
            Customer(name: "İSPARTA ŞAHİN", phone: "05329998877")));

        var second = Single(output, ImportTable.Customers, 3);
        second.Normalized.Action.Should().Be(ImportRowAction.Skip);
        second.Normalized.MergedIntoRow.Should().Be(2);
        Codes(second).Should().Equal("Imports.DuplicateInFile");

        var fourth = Single(output, ImportTable.Customers, 5);
        fourth.Normalized.Action.Should().Be(ImportRowAction.Skip);
        fourth.Normalized.MergedIntoRow.Should().Be(4);
    }

    [Fact]
    public void Customer_Duplicate_Name_And_Email_In_File_Should_Merge_Without_Phone()
    {
        var output = Run(Customers(
            Customer(name: "Ayşe Yılmaz", phone: null, email: "a@x.com"),
            Customer(name: "Ayşe Yılmaz", phone: null, email: "A@X.com")));

        Codes(Single(output, ImportTable.Customers, 3)).Should().Equal("Imports.DuplicateInFile");
    }

    [Fact]
    public void Customer_Same_Phone_Different_Name_Should_Create_With_Shared_Phone_Info()
    {
        var output = Run(Customers(
            Customer(name: "Ayşe Yılmaz", phone: "05321234567"),
            Customer(name: "Ali Yılmaz", phone: "05321234567")));

        var second = Single(output, ImportTable.Customers, 3);
        second.Status.Should().Be(ImportRowValidationStatus.Valid, "SharedPhone yalnız bilgidir");
        second.Issues.Single().Should().Match<ImportIssue>(i => i.Code == "Imports.SharedPhone" && i.Severity == ImportIssueSeverity.Info);
        second.Normalized.Action.Should().Be(ImportRowAction.Create);
    }

    [Fact]
    public void Customer_Shared_Phone_With_Existing_Client_Should_Be_Info()
    {
        var existing = new ImportExistingData([new(Guid.NewGuid(), "Başka Kişi", "905321234567", null)], [], []);

        var r = Single(Run(Customers(Customer(phone: "05321234567")), existing: existing), ImportTable.Customers);

        Codes(r).Should().Equal("Imports.SharedPhone");
        r.Normalized.Action.Should().Be(ImportRowAction.Create);
    }

    [Fact]
    public void Customer_Without_Phone_And_Email_With_Same_Name_Should_Warn_Possible_Duplicate()
    {
        var output = Run(Customers(
            Customer(name: "Ayşe Yılmaz", phone: null),
            Customer(name: "ayşe yılmaz", phone: null)));

        Codes(Single(output, ImportTable.Customers, 2)).Should().BeEmpty();
        var second = Single(output, ImportTable.Customers, 3);
        second.Status.Should().Be(ImportRowValidationStatus.Warning);
        Codes(second).Should().Equal("Imports.PossibleDuplicate");
        second.Normalized.Action.Should().Be(ImportRowAction.Create, "ayırt edilemeyen müşteri birleştirilmez");
    }

    [Fact]
    public void Customer_Without_Phone_And_Email_Same_Name_As_Existing_Client_Should_Warn()
    {
        var existing = new ImportExistingData([new(Guid.NewGuid(), "Ayşe Yılmaz", null, null)], [], []);

        var r = Single(Run(Customers(Customer(name: "Ayşe Yılmaz", phone: null)), existing: existing), ImportTable.Customers);

        Codes(r).Should().Equal("Imports.PossibleDuplicate");
    }

    // ---------------------------------------------------------------- K4: mevcut müşteri

    [Fact]
    public void Customer_Existing_Name_And_Phone_Should_Match_By_Default_K4()
    {
        var clientId = Guid.NewGuid();
        var existing = new ImportExistingData([new(clientId, "Ayşe Yılmaz", "905321234567", "eski@x.com")], [], []);

        var r = Single(Run(Customers(Customer(name: "ayşe yılmaz", phone: "0532 123 45 67", email: "yeni@x.com")), existing: existing), ImportTable.Customers);

        r.Status.Should().Be(ImportRowValidationStatus.Valid);
        Codes(r).Should().Equal("Imports.MatchedExisting");
        r.Normalized.Action.Should().Be(ImportRowAction.Match);
        r.Normalized.TargetId.Should().Be(clientId);
    }

    [Fact]
    public void Customer_Existing_Name_And_Email_Should_Match_When_Phone_Is_Absent()
    {
        var clientId = Guid.NewGuid();
        var existing = new ImportExistingData([new(clientId, "Ayşe Yılmaz", null, "ayse@x.com")], [], []);

        var r = Single(Run(Customers(Customer(phone: null, email: "AYSE@x.com")), existing: existing), ImportTable.Customers);

        r.Normalized.Action.Should().Be(ImportRowAction.Match);
        r.Normalized.TargetId.Should().Be(clientId);
    }

    [Fact]
    public void Customer_Existing_Same_Phone_Different_Name_Should_Not_Match()
    {
        var existing = new ImportExistingData([new(Guid.NewGuid(), "Başka Kişi", "905321234567", null)], [], []);

        Single(Run(Customers(Customer(name: "Ayşe Yılmaz")), existing: existing), ImportTable.Customers)
            .Normalized.Action.Should().Be(ImportRowAction.Create);
    }

    [Fact]
    public void Customer_Existing_With_Skip_Mode_Should_Skip_Row_But_Not_Error()
    {
        var existing = new ImportExistingData([new(Guid.NewGuid(), "Ayşe Yılmaz", "905321234567", null)], [], []);

        var r = Single(Run(Customers(Customer()), existing: existing, onDuplicateClient: ImportOptionsDocument.Skip), ImportTable.Customers);

        r.Status.Should().Be(ImportRowValidationStatus.Valid);
        r.Normalized.Action.Should().Be(ImportRowAction.Skip);
    }

    [Fact]
    public void Customer_Existing_With_Error_Mode_Should_Be_Error()
    {
        var existing = new ImportExistingData([new(Guid.NewGuid(), "Ayşe Yılmaz", "905321234567", null)], [], []);

        var r = Single(Run(Customers(Customer()), existing: existing, onDuplicateClient: ImportOptionsDocument.Error), ImportTable.Customers);

        r.Status.Should().Be(ImportRowValidationStatus.Error);
        Codes(r).Should().Equal("Imports.DuplicateClient");
        r.Normalized.Action.Should().BeNull();
    }

    // ================================================================ Hasta

    [Fact]
    public void Pet_Linked_By_Customer_Reference_Should_Be_Created_With_Normalized_Fields()
    {
        var output = Run(
            customers: Customers(Customer(no: "M-1")),
            pets: Pets(Pet(
                customerNo: "M-1", petNo: "H-1", name: "  Pamuk ", species: "KEDİ", breed: "Tekir", gender: "dişi",
                birth: "15.03.2021", neutered: "Evet", chip: "985 112 345 678 901", weight: "4,5", passport: "K-7", note: "Sakin")));

        var r = Single(output, ImportTable.Pets);
        r.Status.Should().Be(ImportRowValidationStatus.Valid);
        var n = r.Normalized.Should().BeOfType<ImportNormalizedPet>().Subject;
        n.Action.Should().Be(ImportRowAction.Create);
        n.OwnerKey.Should().Be("file:2");
        n.Name.Should().Be("Pamuk");
        n.SpeciesId.Should().Be(Cat);
        n.BreedId.Should().Be(Tekir);
        n.Breed.Should().BeNull();
        n.Gender.Should().Be(PetGender.Female);
        n.BirthDate.Should().Be(new DateOnly(2021, 3, 15));
        n.IsNeutered.Should().BeTrue();
        n.Microchip.Should().Be("985112345678901");
        n.Weight.Should().Be(4.5m);
        n.PassportOrTagNumber.Should().Be("K-7");
        n.Notes.Should().Be("Sakin");
    }

    [Fact]
    public void Pet_Linked_By_Owner_Name_And_Phone_To_File_Customer()
    {
        var output = Run(
            customers: Customers(Customer(name: "Ayşe Yılmaz", phone: "05321234567")),
            pets: Pets(Pet(owner: "ayşe yılmaz", ownerPhone: "+90 532 123 45 67")));

        var r = Single(output, ImportTable.Pets);
        r.Status.Should().Be(ImportRowValidationStatus.Valid);
        ((ImportNormalizedPet)r.Normalized).OwnerKey.Should().Be("file:2");
    }

    [Fact]
    public void Pet_Owner_Not_In_File_Should_Link_To_Existing_Client_By_Name_And_Phone()
    {
        var clientId = Guid.NewGuid();
        var existing = new ImportExistingData([new(clientId, "Ayşe Yılmaz", "905321234567", null)], [], []);

        var r = Single(
            Run(pets: Pets(Pet(owner: "Ayşe Yılmaz", ownerPhone: "05321234567")), existing: existing, mapping: Mapping(pets: PetFields)),
            ImportTable.Pets);

        r.Status.Should().Be(ImportRowValidationStatus.Valid);
        ((ImportNormalizedPet)r.Normalized).OwnerKey.Should().Be($"db:{clientId}");
        r.Normalized.Action.Should().Be(ImportRowAction.Create);
    }

    [Fact]
    public void Pet_Reference_Not_In_Customer_Table_Should_Be_Owner_Not_Found()
    {
        var output = Run(
            customers: Customers(Customer(no: "M-1")),
            pets: Pets(Pet(customerNo: "M-999")));

        var r = Single(output, ImportTable.Pets);
        r.Status.Should().Be(ImportRowValidationStatus.Error);
        Codes(r).Should().Equal("Imports.OwnerNotFound");
    }

    [Fact]
    public void Pet_Of_Invalid_Customer_Row_Should_Cascade_As_Owner_Row_Invalid()
    {
        var output = Run(
            customers: Customers(Customer(no: "M-1", name: null)),
            pets: Pets(Pet(customerNo: "M-1")));

        Codes(Single(output, ImportTable.Pets)).Should().Equal("Imports.OwnerRowInvalid");
    }

    [Fact]
    public void Pet_Owner_Without_Phone_Should_Link_To_Unique_File_Customer_By_Name()
    {
        var output = Run(
            customers: Customers(Customer(name: "Ayşe Yılmaz", phone: "02121234567")), // sabit hat: telefonsuz aktarılır
            pets: Pets(Pet(owner: "Ayşe Yılmaz", ownerPhone: "02121234567")));

        var r = Single(output, ImportTable.Pets);
        r.Status.Should().Be(ImportRowValidationStatus.Warning, "sahip telefonu geçersiz uyarısı");
        Codes(r).Should().Equal("Imports.PhoneInvalid");
        ((ImportNormalizedPet)r.Normalized).OwnerKey.Should().Be("file:2");
    }

    [Fact]
    public void Pet_Owner_Without_Phone_And_Ambiguous_Name_Should_Be_Owner_Not_Found()
    {
        var output = Run(
            customers: Customers(
                Customer(name: "Ayşe Yılmaz", phone: "05321234567"),
                Customer(name: "Ayşe Yılmaz", phone: "05331234567")),
            pets: Pets(Pet(owner: "Ayşe Yılmaz", ownerPhone: null)));

        Codes(Single(output, ImportTable.Pets)).Should().Equal("Imports.OwnerNotFound");
    }

    [Fact]
    public void Pet_Linked_To_Matched_Existing_Client_Should_Use_Database_Owner()
    {
        var clientId = Guid.NewGuid();
        var existing = new ImportExistingData([new(clientId, "Ayşe Yılmaz", "905321234567", "a@x.com")], [], []);

        var output = Run(
            customers: Customers(Customer(no: "M-1")),
            pets: Pets(Pet(customerNo: "M-1")),
            existing: existing);

        ((ImportNormalizedPet)Single(output, ImportTable.Pets).Normalized).OwnerKey.Should().Be($"db:{clientId}");
    }

    [Fact]
    public void Pet_Required_Fields_Missing_Should_Be_Errors()
    {
        var output = Run(
            customers: Customers(Customer(no: "M-1")),
            pets: Pets(Pet(customerNo: "M-1", name: null, species: null)));

        var r = Single(output, ImportTable.Pets);
        r.Status.Should().Be(ImportRowValidationStatus.Error);
        r.Issues.Where(i => i.Code == "Imports.RequiredMissing").Select(i => i.Field).Should().BeEquivalentTo("HayvanAdi", "Tur");
    }

    [Fact]
    public void Pet_Unmapped_Species_Should_Be_Error_With_Source_Value_K6()
    {
        var output = Run(
            customers: Customers(Customer(no: "M-1")),
            pets: Pets(Pet(customerNo: "M-1", species: "Ejderha")));

        var r = Single(output, ImportTable.Pets);
        r.Status.Should().Be(ImportRowValidationStatus.Error);
        r.Issues.Single(i => i.Code == "Imports.SpeciesUnmapped").Value.Should().Be("Ejderha");
    }

    [Fact]
    public void Pet_Species_Mapped_By_User_Should_Resolve()
    {
        var mapping = Mapping(CustomerFields, PetFields, null, new ImportValueMapping("species", "Kopek", Dog));
        var output = Run(
            customers: Customers(Customer(no: "M-1")),
            pets: Pets(Pet(customerNo: "M-1", species: "Kopek")),
            mapping: mapping);

        ((ImportNormalizedPet)Single(output, ImportTable.Pets).Normalized).SpeciesId.Should().Be(Dog);
    }

    [Fact]
    public void Pet_Species_Skipped_By_User_Should_Skip_Row_With_Warning_And_Cascade_To_Vaccinations()
    {
        var mapping = Mapping(CustomerFields, PetFields, VaccinationFields, new ImportValueMapping("species", "Ejderha", null));
        var output = Run(
            customers: Customers(Customer(no: "M-1")),
            pets: Pets(Pet(customerNo: "M-1", petNo: "H-1", species: "Ejderha")),
            vaccinations: Vaccinations(Vaccination(petNo: "H-1")),
            mapping: mapping);

        var pet = Single(output, ImportTable.Pets);
        pet.Status.Should().Be(ImportRowValidationStatus.Warning);
        Codes(pet).Should().Equal("Imports.ValueSkipped");
        pet.Normalized.Action.Should().Be(ImportRowAction.Skip);

        var vaccination = Single(output, ImportTable.Vaccinations);
        vaccination.Status.Should().Be(ImportRowValidationStatus.Warning);
        Codes(vaccination).Should().Equal("Imports.ParentSkipped");
        vaccination.Normalized.Action.Should().Be(ImportRowAction.Skip);
    }

    [Fact]
    public void Pet_Breed_Not_In_Catalog_Should_Be_Free_Text_Info_Only()
    {
        var output = Run(
            customers: Customers(Customer(no: "M-1")),
            pets: Pets(Pet(customerNo: "M-1", breed: "Sokak kedisi")));

        var r = Single(output, ImportTable.Pets);
        r.Status.Should().Be(ImportRowValidationStatus.Valid);
        r.Issues.Single().Should().Match<ImportIssue>(i => i.Code == "Imports.BreedFreeText" && i.Severity == ImportIssueSeverity.Info);
        var n = (ImportNormalizedPet)r.Normalized;
        n.Breed.Should().Be("Sokak kedisi");
        n.BreedId.Should().BeNull();
    }

    [Fact]
    public void Pet_Breed_Mapped_To_Other_Species_Should_Mismatch()
    {
        var mapping = Mapping(CustomerFields, PetFields, null, new ImportValueMapping("breed", "Tekir", Tekir));
        var output = Run(
            customers: Customers(Customer(no: "M-1")),
            pets: Pets(Pet(customerNo: "M-1", species: "Köpek", breed: "Tekir")),
            mapping: mapping);

        var r = Single(output, ImportTable.Pets);
        r.Status.Should().Be(ImportRowValidationStatus.Error);
        Codes(r).Should().Equal("Pets.BreedSpeciesMismatch");
    }

    [Fact]
    public void Pet_Color_Not_In_Catalog_Should_Be_Prepended_To_Notes()
    {
        var output = Run(
            customers: Customers(Customer(no: "M-1")),
            pets: Pets(Pet(customerNo: "M-1", color: "Sarı beyaz", note: "Sakin")));

        var r = Single(output, ImportTable.Pets);
        r.Status.Should().Be(ImportRowValidationStatus.Valid);
        ((ImportNormalizedPet)r.Normalized).Notes.Should().Be("Renk: Sarı beyaz\nSakin");
        Codes(r).Should().Equal("Imports.ColorFreeText");
    }

    [Fact]
    public void Pet_Notes_With_Color_Over_2000_Should_Be_Error()
    {
        var output = Run(
            customers: Customers(Customer(no: "M-1")),
            pets: Pets(Pet(customerNo: "M-1", color: "Sarı", note: new string('n', 2000))));

        Codes(Single(output, ImportTable.Pets)).Should().Contain("Imports.FieldTooLong");
    }

    [Theory]
    [InlineData("belirsiz", "Imports.GenderUnrecognized", "Cinsiyet")]
    public void Pet_Unrecognized_Gender_Should_Warn(string gender, string code, string field)
    {
        var output = Run(
            customers: Customers(Customer(no: "M-1")),
            pets: Pets(Pet(customerNo: "M-1", gender: gender)));

        var r = Single(output, ImportTable.Pets);
        r.Status.Should().Be(ImportRowValidationStatus.Warning);
        r.Issues.Single().Should().Match<ImportIssue>(i => i.Code == code && i.Field == field);
        ((ImportNormalizedPet)r.Normalized).Gender.Should().BeNull();
    }

    [Fact]
    public void Pet_Unrecognized_Neutered_And_Invalid_Weight_Should_Warn()
    {
        var output = Run(
            customers: Customers(Customer(no: "M-1")),
            pets: Pets(Pet(customerNo: "M-1", neutered: "belki", weight: "çok")));

        var r = Single(output, ImportTable.Pets);
        r.Status.Should().Be(ImportRowValidationStatus.Warning);
        Codes(r).Should().BeEquivalentTo("Imports.NeuteredUnrecognized", "Imports.WeightInvalid");
    }

    // ---------------------------------------------------------------- Hasta tarihleri

    [Fact]
    public void Pet_Future_Birth_Date_Should_Be_Error_But_Today_Is_Allowed()
    {
        var output = Run(
            customers: Customers(Customer(no: "M-1")),
            pets: Pets(
                Pet(customerNo: "M-1", name: "Gelecek", birth: "15.10.2026"),
                Pet(customerNo: "M-1", name: "Bugün", birth: "14.10.2026")));

        var future = Single(output, ImportTable.Pets, 2);
        future.Status.Should().Be(ImportRowValidationStatus.Error);
        Codes(future).Should().Equal("Pets.BirthDateInFuture");
        Single(output, ImportTable.Pets, 3).Status.Should().Be(ImportRowValidationStatus.Valid);
    }

    [Fact]
    public void Pet_Very_Old_Birth_Date_Should_Warn_Implausible()
    {
        var output = Run(
            customers: Customers(Customer(no: "M-1")),
            pets: Pets(Pet(customerNo: "M-1", birth: "01.01.1980")));

        var r = Single(output, ImportTable.Pets);
        r.Status.Should().Be(ImportRowValidationStatus.Warning);
        Codes(r).Should().Equal("Imports.BirthDateImplausible");
        ((ImportNormalizedPet)r.Normalized).BirthDate.Should().Be(new DateOnly(1980, 1, 1));
    }

    [Theory]
    [InlineData("3.5.24", "Imports.DateFormatAmbiguous")]
    [InlineData("31.02.2020", "Imports.DateInvalid")]
    public void Pet_Bad_Birth_Date_Should_Be_Error(string birth, string code)
    {
        var output = Run(
            customers: Customers(Customer(no: "M-1")),
            pets: Pets(Pet(customerNo: "M-1", birth: birth)));

        var r = Single(output, ImportTable.Pets);
        r.Status.Should().Be(ImportRowValidationStatus.Error);
        r.Issues.Single().Should().Match<ImportIssue>(i => i.Code == code && i.Value == birth);
    }

    // ---------------------------------------------------------------- Mikroçip (K10)

    [Fact]
    public void Pet_Microchip_Scientific_Notation_And_Invalid_Should_Be_Distinct_Errors()
    {
        var output = Run(
            customers: Customers(Customer(no: "M-1")),
            pets: Pets(
                Pet(customerNo: "M-1", name: "A", chip: "9,85121E+14"),
                Pet(customerNo: "M-1", name: "B", chip: "98511X")));

        Codes(Single(output, ImportTable.Pets, 2)).Should().Equal("Imports.MicrochipScientificNotation");
        Codes(Single(output, ImportTable.Pets, 3)).Should().Equal("Imports.MicrochipInvalid");
    }

    [Fact]
    public void Pet_Microchip_Not_15_Digits_Should_Warn_Only()
    {
        var output = Run(
            customers: Customers(Customer(no: "M-1")),
            pets: Pets(Pet(customerNo: "M-1", chip: "12345678")));

        var r = Single(output, ImportTable.Pets);
        r.Status.Should().Be(ImportRowValidationStatus.Warning);
        Codes(r).Should().Equal("Imports.MicrochipLength");
        r.Normalized.Action.Should().Be(ImportRowAction.Create);
    }

    [Fact]
    public void Pet_Same_Microchip_On_Two_Pets_In_File_Should_Error_Second_K10()
    {
        var output = Run(
            customers: Customers(Customer(no: "M-1"), Customer(no: "M-2", name: "Veli Kaya", phone: "05331234567")),
            pets: Pets(
                Pet(customerNo: "M-1", name: "Pamuk", chip: "985112345678901"),
                Pet(customerNo: "M-2", name: "Karamel", chip: "985112345678901")));

        Single(output, ImportTable.Pets, 2).Status.Should().Be(ImportRowValidationStatus.Valid);
        var second = Single(output, ImportTable.Pets, 3);
        second.Status.Should().Be(ImportRowValidationStatus.Error);
        Codes(second).Should().Equal("Imports.MicrochipConflict");
        second.Issues.Single().Message.Should().Contain("2. satır");
    }

    [Fact]
    public void Pet_Microchip_Used_By_Other_Pet_In_Database_Should_Error()
    {
        var clientId = Guid.NewGuid();
        var existing = new ImportExistingData(
            [new(clientId, "Veli Kaya", "905331234567", null)],
            [new(Guid.NewGuid(), clientId, "Karamel", Cat, "985112345678901", null)],
            []);

        var output = Run(
            customers: Customers(Customer(no: "M-1")),
            pets: Pets(Pet(customerNo: "M-1", chip: "985 112 345 678 901")),
            existing: existing);

        var r = Single(output, ImportTable.Pets);
        r.Status.Should().Be(ImportRowValidationStatus.Error);
        Codes(r).Should().Equal("Imports.MicrochipConflict");
    }

    [Fact]
    public void Pet_Matching_Existing_Pet_With_Same_Microchip_Is_Not_A_Conflict()
    {
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var existing = new ImportExistingData(
            [new(clientId, "Ayşe Yılmaz", "905321234567", null)],
            [new(petId, clientId, "Pamuk", Cat, "985112345678901", null)],
            []);

        var output = Run(
            customers: Customers(Customer(no: "M-1")),
            pets: Pets(Pet(customerNo: "M-1", chip: "985112345678901")),
            existing: existing);

        var r = Single(output, ImportTable.Pets);
        r.Status.Should().Be(ImportRowValidationStatus.Valid);
        r.Normalized.Action.Should().Be(ImportRowAction.Match);
        r.Normalized.TargetId.Should().Be(petId);
    }

    // ---------------------------------------------------------------- Hasta mükerrer

    [Fact]
    public void Pet_Duplicate_Owner_Name_Species_In_File_Should_Merge()
    {
        var output = Run(
            customers: Customers(Customer(no: "M-1")),
            pets: Pets(
                Pet(customerNo: "M-1", name: "Pamuk"),
                Pet(customerNo: "M-1", name: "PAMUK")));

        var second = Single(output, ImportTable.Pets, 3);
        Codes(second).Should().Equal("Imports.DuplicateInFile");
        second.Normalized.Action.Should().Be(ImportRowAction.Skip);
        second.Normalized.MergedIntoRow.Should().Be(2);
    }

    [Fact]
    public void Pet_Same_Name_Different_Species_Or_Owner_Is_Not_Duplicate()
    {
        var output = Run(
            customers: Customers(Customer(no: "M-1"), Customer(no: "M-2", name: "Veli Kaya", phone: "05331234567")),
            pets: Pets(
                Pet(customerNo: "M-1", name: "Pamuk", species: "Kedi"),
                Pet(customerNo: "M-1", name: "Pamuk", species: "Köpek"),
                Pet(customerNo: "M-2", name: "Pamuk", species: "Kedi")));

        output.Rows.Where(r => r.Table == ImportTable.Pets).Should().OnlyContain(r => r.Normalized.Action == ImportRowAction.Create);
    }

    [Fact]
    public void Pet_Duplicate_Name_Is_Turkish_Aware_In_File()
    {
        // Müşteri kuralıyla tutarlı: KARAMEL / Karamel ve ISPARTA / Isparta aynı hayvan (eski ToLowerInvariant sınırı kalktı).
        var output = Run(
            customers: Customers(Customer(no: "M-1")),
            pets: Pets(
                Pet(customerNo: "M-1", name: "Karamel"),
                Pet(customerNo: "M-1", name: "KARAMEL"),
                Pet(customerNo: "M-1", name: "Isparta"),
                Pet(customerNo: "M-1", name: "İSPARTA")));

        Codes(Single(output, ImportTable.Pets, 3)).Should().Equal("Imports.DuplicateInFile");
        Codes(Single(output, ImportTable.Pets, 5)).Should().Equal("Imports.DuplicateInFile");
        Single(output, ImportTable.Pets, 5).Normalized.MergedIntoRow.Should().Be(4);
    }

    [Fact]
    public void Pet_Existing_Name_Match_Is_Turkish_Aware()
    {
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var existing = new ImportExistingData(
            [new(clientId, "Ayşe Yılmaz", "905321234567", null)],
            [new(petId, clientId, "Isparta", Cat, null, null)],
            []);

        var output = Run(
            customers: Customers(Customer(no: "M-1")),
            pets: Pets(Pet(customerNo: "M-1", name: "İSPARTA")),
            existing: existing);

        var r = Single(output, ImportTable.Pets);
        r.Normalized.Action.Should().Be(ImportRowAction.Match);
        r.Normalized.TargetId.Should().Be(petId);
    }

    [Fact]
    public void Pet_Existing_For_Matched_Client_Should_Match_Without_Overwrite()
    {
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var existing = new ImportExistingData(
            [new(clientId, "Ayşe Yılmaz", "905321234567", null)],
            [new(petId, clientId, "Pamuk", Cat, null, null)],
            []);

        var output = Run(
            customers: Customers(Customer(no: "M-1")),
            pets: Pets(Pet(customerNo: "M-1", name: "pamuk")),
            existing: existing);

        var r = Single(output, ImportTable.Pets);
        r.Normalized.Action.Should().Be(ImportRowAction.Match);
        r.Normalized.TargetId.Should().Be(petId);
        Codes(r).Should().Equal("Imports.MatchedExisting");
    }

    [Fact]
    public void Pet_Duplicate_Reference_Should_Error()
    {
        var output = Run(
            customers: Customers(Customer(no: "M-1")),
            pets: Pets(
                Pet(customerNo: "M-1", petNo: "H-1", name: "Pamuk"),
                Pet(customerNo: "M-1", petNo: "H-1", name: "Karamel")));

        Codes(Single(output, ImportTable.Pets, 3)).Should().Contain("Imports.DuplicateReference");
    }

    // ================================================================ Aşı

    private static ImportValidationOutput WithPet(string?[] vaccination, ImportExistingData? existing = null, string?[]? petRow = null)
        => Run(
            customers: Customers(Customer(no: "M-1")),
            pets: Pets(petRow ?? Pet(customerNo: "M-1", petNo: "H-1", species: "Kedi", birth: "01.01.2020")),
            vaccinations: Vaccinations(vaccination),
            existing: existing);

    [Fact]
    public void Vaccination_Linked_By_Pet_Reference_Should_Be_Created_With_Utc_Noon_Dates()
    {
        var output = WithPet(Vaccination(petNo: "H-1", due: "10.04.2025", serial: "L-5", by: "Dr. Örnek", note: "Doz 1"));

        var r = Single(output, ImportTable.Vaccinations);
        r.Status.Should().Be(ImportRowValidationStatus.Valid);
        var n = r.Normalized.Should().BeOfType<ImportNormalizedVaccination>().Subject;
        n.Action.Should().Be(ImportRowAction.Create);
        n.VaccineDefinitionId.Should().Be(Rabies);
        n.VaccineName.Should().Be("Kuduz");
        n.AppliedAt.Should().Be(new DateOnly(2024, 4, 10));
        n.AppliedAtUtc.Should().Be(new DateTime(2024, 4, 10, 9, 0, 0, DateTimeKind.Utc), "İstanbul 12:00 = 09:00 UTC");
        n.DueAtUtc.Should().Be(new DateTime(2025, 4, 10, 9, 0, 0, DateTimeKind.Utc));
        n.Notes.Should().Be("Seri: L-5; Uygulayan: Dr. Örnek; Doz 1");
        n.PetKey.Should().Be("file:2");
    }

    [Fact]
    public void Vaccination_Linked_By_Owner_Pet_Name_And_Species_Should_Resolve()
    {
        var output = Run(
            customers: Customers(Customer(no: "M-1")),
            pets: Pets(Pet(customerNo: "M-1", name: "Pamuk", species: "Kedi")),
            vaccinations: Vaccinations(Vaccination(owner: "Ayşe Yılmaz", ownerPhone: "05321234567", petName: "pamuk", species: "KEDİ")),
            mapping: Mapping(CustomerFields, PetFields, VaccinationFields));

        var r = Single(output, ImportTable.Vaccinations);
        r.Status.Should().Be(ImportRowValidationStatus.Valid);
        ((ImportNormalizedVaccination)r.Normalized).PetKey.Should().Be("file:2");
    }

    [Fact]
    public void Vaccination_Linked_To_Existing_Pet_Of_Existing_Client()
    {
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var existing = new ImportExistingData(
            [new(clientId, "Ayşe Yılmaz", "905321234567", null)],
            [new(petId, clientId, "Pamuk", Cat, null, null)],
            []);

        var output = Run(
            vaccinations: Vaccinations(Vaccination(owner: "Ayşe Yılmaz", ownerPhone: "05321234567", petName: "Pamuk", species: "Kedi")),
            existing: existing);

        var r = Single(output, ImportTable.Vaccinations);
        r.Status.Should().Be(ImportRowValidationStatus.Valid);
        ((ImportNormalizedVaccination)r.Normalized).PetKey.Should().Be($"db:{petId}");
    }

    [Fact]
    public void Vaccination_Pet_Not_Found_Should_Be_Error()
    {
        var output = WithPet(Vaccination(petNo: "H-404"));

        Codes(Single(output, ImportTable.Vaccinations)).Should().Equal("Imports.PetNotFound");
    }

    [Fact]
    public void Vaccination_Of_Invalid_Pet_Row_Should_Cascade_As_Pet_Row_Invalid()
    {
        var output = WithPet(
            Vaccination(petNo: "H-1"),
            petRow: Pet(customerNo: "M-1", petNo: "H-1", species: "Ejderha"));

        Codes(Single(output, ImportTable.Vaccinations)).Should().Equal("Imports.PetRowInvalid");
    }

    [Fact]
    public void Vaccination_Unmapped_Vaccine_Should_Be_Error_With_Source_Value()
    {
        var output = WithPet(Vaccination(petNo: "H-1", vaccine: "Süper Aşı"));

        var r = Single(output, ImportTable.Vaccinations);
        r.Status.Should().Be(ImportRowValidationStatus.Error);
        r.Issues.Single(i => i.Code == "Imports.VaccineUnmapped").Value.Should().Be("Süper Aşı");
    }

    [Fact]
    public void Vaccination_Vaccine_For_Other_Species_Should_Not_Auto_Map()
    {
        // Bronşin yalnız köpek aşısıdır; kedi hastasında otomatik eşleşmez.
        var output = WithPet(Vaccination(petNo: "H-1", vaccine: "Bronşin"));

        Codes(Single(output, ImportTable.Vaccinations)).Should().Equal("Imports.VaccineUnmapped");
    }

    [Fact]
    public void Vaccination_User_Mapped_Vaccine_Of_Other_Species_Should_Be_Invalid_Species()
    {
        var mapping = Mapping(CustomerFields, PetFields, VaccinationFields, new ImportValueMapping("vaccine", "Bronşin", Bronchine));
        var output = Run(
            customers: Customers(Customer(no: "M-1")),
            pets: Pets(Pet(customerNo: "M-1", petNo: "H-1", species: "Kedi")),
            vaccinations: Vaccinations(Vaccination(petNo: "H-1", vaccine: "Bronşin")),
            mapping: mapping);

        Codes(Single(output, ImportTable.Vaccinations)).Should().Equal("VaccineDefinitions.InvalidSpecies");
    }

    [Fact]
    public void Vaccination_Vaccine_Skipped_By_User_Should_Skip_With_Warning()
    {
        var mapping = Mapping(CustomerFields, PetFields, VaccinationFields, new ImportValueMapping("vaccine", "Eski Aşı", null));
        var output = Run(
            customers: Customers(Customer(no: "M-1")),
            pets: Pets(Pet(customerNo: "M-1", petNo: "H-1")),
            vaccinations: Vaccinations(Vaccination(petNo: "H-1", vaccine: "Eski Aşı")),
            mapping: mapping);

        var r = Single(output, ImportTable.Vaccinations);
        r.Status.Should().Be(ImportRowValidationStatus.Warning);
        Codes(r).Should().Equal("Imports.ValueSkipped");
        r.Normalized.Action.Should().Be(ImportRowAction.Skip);
    }

    // ---------------------------------------------------------------- Aşı tarihleri

    [Theory]
    [InlineData("15.10.2026", "Vaccinations.AppliedAtMustNotBeFuture")] // yarın
    [InlineData("31.12.1989", "Imports.DateOutOfRange")]
    [InlineData("3.5.24", "Imports.DateFormatAmbiguous")]
    [InlineData("31.02.2024", "Imports.DateInvalid")]
    public void Vaccination_Applied_Date_Rules(string applied, string code)
    {
        var r = Single(WithPet(Vaccination(petNo: "H-1", applied: applied)), ImportTable.Vaccinations);

        r.Status.Should().Be(ImportRowValidationStatus.Error);
        Codes(r).Should().Equal(code);
    }

    [Fact]
    public void Vaccination_Applied_Today_And_On_Min_Date_Are_Allowed()
    {
        var output = WithPet(Vaccination(petNo: "H-1", applied: "14.10.2026"));
        Single(output, ImportTable.Vaccinations).Status.Should().Be(ImportRowValidationStatus.Valid);

        var min = WithPet(Vaccination(petNo: "H-1", applied: "01.01.1990"), petRow: Pet(customerNo: "M-1", petNo: "H-1"));
        Single(min, ImportTable.Vaccinations).Status.Should().Be(ImportRowValidationStatus.Valid);
    }

    [Fact]
    public void Vaccination_Required_Fields_Missing_Should_Be_Errors()
    {
        var r = Single(WithPet(Vaccination(petNo: "H-1", vaccine: null, applied: null)), ImportTable.Vaccinations);

        r.Status.Should().Be(ImportRowValidationStatus.Error);
        r.Issues.Select(i => i.Field).Should().BeEquivalentTo("AsiAdi", "UygulamaTarihi");
    }

    [Fact]
    public void Vaccination_Applied_Before_Birth_Should_Warn()
    {
        var r = Single(WithPet(Vaccination(petNo: "H-1", applied: "01.01.2019")), ImportTable.Vaccinations);

        r.Status.Should().Be(ImportRowValidationStatus.Warning);
        Codes(r).Should().Equal("Imports.AppliedBeforeBirth");
        r.Normalized.Action.Should().Be(ImportRowAction.Create);
    }

    [Theory]
    [InlineData("10.04.2024", "Vaccinations.DueAtMustBeAfterAppliedAt")] // aynı gün
    [InlineData("09.04.2024", "Vaccinations.DueAtMustBeAfterAppliedAt")] // öncesi
    [InlineData("15.10.2031", "Vaccinations.DueTooFarInFuture")] // 5 yıldan uzak
    [InlineData("bugün", "Imports.DateInvalid")]
    public void Vaccination_Due_Date_Rules(string due, string code)
    {
        var r = Single(WithPet(Vaccination(petNo: "H-1", applied: "10.04.2024", due: due)), ImportTable.Vaccinations);

        r.Status.Should().Be(ImportRowValidationStatus.Error);
        Codes(r).Should().Equal(code);
    }

    [Fact]
    public void Vaccination_Past_Due_Date_Is_Valid_And_Future_Within_Five_Years_Is_Valid()
    {
        var past = Single(WithPet(Vaccination(petNo: "H-1", applied: "10.04.2020", due: "10.04.2021")), ImportTable.Vaccinations);
        past.Status.Should().Be(ImportRowValidationStatus.Valid, "geçmişte kalan sonraki-aşı tarihi geçerlidir");

        var edge = Single(WithPet(Vaccination(petNo: "H-1", applied: "10.04.2024", due: "14.10.2031")), ImportTable.Vaccinations);
        edge.Status.Should().Be(ImportRowValidationStatus.Valid, "tam 5 yıl sınır içidir");
    }

    // ---------------------------------------------------------------- Aşı mükerrer

    [Fact]
    public void Vaccination_Duplicate_In_File_Should_Skip_As_Info()
    {
        var output = Run(
            customers: Customers(Customer(no: "M-1")),
            pets: Pets(Pet(customerNo: "M-1", petNo: "H-1")),
            vaccinations: Vaccinations(
                Vaccination(petNo: "H-1", vaccine: "Kuduz", applied: "10.04.2024"),
                Vaccination(petNo: "H-1", vaccine: "kuduz aşısı", applied: "10.04.2024"),
                Vaccination(petNo: "H-1", vaccine: "Kuduz", applied: "11.04.2024")));

        Single(output, ImportTable.Vaccinations, 2).Normalized.Action.Should().Be(ImportRowAction.Create);
        var duplicate = Single(output, ImportTable.Vaccinations, 3);
        duplicate.Status.Should().Be(ImportRowValidationStatus.Valid, "DuplicateVaccination yalnız bilgidir");
        Codes(duplicate).Should().Equal("Imports.DuplicateVaccination");
        duplicate.Normalized.Action.Should().Be(ImportRowAction.Skip);
        Single(output, ImportTable.Vaccinations, 4).Normalized.Action.Should().Be(ImportRowAction.Create);
    }

    [Fact]
    public void Vaccination_Already_In_Database_Should_Skip()
    {
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var existing = new ImportExistingData(
            [new(clientId, "Ayşe Yılmaz", "905321234567", null)],
            [new(petId, clientId, "Pamuk", Cat, null, null)],
            [new(petId, Rabies, new DateTime(2024, 4, 10, 9, 0, 0, DateTimeKind.Utc))]);

        var output = Run(
            customers: Customers(Customer(no: "M-1")),
            pets: Pets(Pet(customerNo: "M-1", petNo: "H-1", name: "Pamuk")),
            vaccinations: Vaccinations(Vaccination(petNo: "H-1")),
            existing: existing);

        var r = Single(output, ImportTable.Vaccinations);
        Codes(r).Should().Equal("Imports.DuplicateVaccination");
        r.Normalized.Action.Should().Be(ImportRowAction.Skip);
    }

    // ================================================================ Genel değişmezler

    [Fact]
    public void Counts_Should_Satisfy_Invariants_On_Mixed_File()
    {
        var output = Run(
            customers: Customers(
                Customer(no: "M-1"),
                Customer(no: "M-2", name: "Veli Kaya", phone: "02121234567"),
                Customer(no: "M-3", name: null),
                Customer(no: "M-4", name: "Ayşe Yılmaz", phone: "05321234567")),
            pets: Pets(
                Pet(customerNo: "M-1", petNo: "H-1"),
                Pet(customerNo: "M-2", petNo: "H-2", name: "Karamel"),
                Pet(customerNo: "M-3", petNo: "H-3", name: "Hatalı Sahip"),
                Pet(customerNo: "M-1", petNo: "H-4", species: "Ejderha")),
            vaccinations: Vaccinations(
                Vaccination(petNo: "H-1"),
                Vaccination(petNo: "H-1"),
                Vaccination(petNo: "H-2", applied: "yarın"),
                Vaccination(petNo: "H-4")));

        foreach (var table in ImportCanonicalNames.Tables)
        {
            var c = output.CountsOf(table);
            (c.Valid + c.Warning + c.Error).Should().Be(c.Total, $"{table}: valid+warning+error=total");
            (c.WillCreate + c.WillMatch + c.WillSkip).Should().Be(c.Valid + c.Warning, $"{table}: eylemler kabul edilen satırlara eşit");
        }

        output.Rows.Where(r => r.Status == ImportRowValidationStatus.Error)
            .Should().OnlyContain(r => r.Normalized.Action == null, "hatalı satırın eylemi yoktur");
    }

    [Fact]
    public void Every_Emitted_Code_Should_Belong_To_The_Rule_Catalog_And_Keep_Its_Severity()
    {
        var output = Run(
            customers: Customers(
                Customer(no: "M-1", phone: "0212", email: "x@"),
                Customer(no: "M-1", name: "Veli Kaya", phone: null),
                Customer(no: "M-3", name: null)),
            pets: Pets(
                Pet(customerNo: "M-1", petNo: "H-1", species: "Ejderha", birth: "3.5.24", chip: "9,8E+14"),
                Pet(customerNo: "M-404", gender: "x", neutered: "y", weight: "z")),
            vaccinations: Vaccinations(Vaccination(petNo: "H-9", vaccine: "Bilinmeyen", applied: "31.02.2024")));

        var issues = output.Rows.SelectMany(r => r.Issues).ToList();
        issues.Should().NotBeEmpty();
        foreach (var issue in issues)
        {
            var rule = ImportRules.Find(issue.Code);
            rule.Should().NotBeNull($"{issue.Code} katalogda olmalı");
            issue.Severity.Should().Be(rule!.Severity, issue.Code);
            issue.Message.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Rule_Catalog_Codes_Should_Be_Unique()
    {
        ImportRules.All.Select(r => r.Code).Should().OnlyHaveUniqueItems();
        ImportRules.All.Should().OnlyContain(r => r.Code.Contains('.') && r.Message.Length > 0);
    }

    [Fact]
    public void Identical_Input_Should_Produce_Identical_Results()
    {
        ImportValidationOutput Once() => Run(
            customers: Customers(Customer(no: "M-1"), Customer(no: "M-2", name: "Ali", phone: "05331234567")),
            pets: Pets(Pet(customerNo: "M-1", petNo: "H-1"), Pet(customerNo: "M-2", petNo: "H-2", name: "Karamel")),
            vaccinations: Vaccinations(Vaccination(petNo: "H-1"), Vaccination(petNo: "H-2", applied: "yarın")));

        var a = Once().Rows.Select(r => (r.Table, r.RowNumber, r.Status, ImportIssueJson.Serialize(r.Issues), ImportNormalizedJson.Serialize(r.Normalized)));
        var b = Once().Rows.Select(r => (r.Table, r.RowNumber, r.Status, ImportIssueJson.Serialize(r.Issues), ImportNormalizedJson.Serialize(r.Normalized)));

        a.Should().Equal(b);
    }

    [Fact]
    public void Link_Columns_Without_Matching_Table_Should_Fall_Back_To_Owner_Pair()
    {
        // MusteriNo eşlenmiş ama müşteri tablosu yok: ref kullanılamaz, sahip adı+telefon denenir (kiracıdaki müşteri).
        var clientId = Guid.NewGuid();
        var existing = new ImportExistingData([new(clientId, "Ayşe Yılmaz", "905321234567", null)], [], []);

        var r = Single(
            Run(
                pets: Pets(Pet(customerNo: "M-1", owner: "Ayşe Yılmaz", ownerPhone: "05321234567")),
                mapping: Mapping(pets: PetFields),
                existing: existing),
            ImportTable.Pets);

        r.Status.Should().Be(ImportRowValidationStatus.Valid);
        ((ImportNormalizedPet)r.Normalized).OwnerKey.Should().Be($"db:{clientId}");
    }

    // ================================================================ Ön koşullar

    [Fact]
    public void Preconditions_Pets_Linked_Only_By_Customer_Number_Need_Customer_Table_With_Number()
    {
        var petsByNumber = new[] { "MusteriNo", "HayvanAdi", "Tur" };
        var tables = new[] { ImportTable.Pets };

        ImportPreconditions.Check(Mapping(pets: petsByNumber), tables)
            .Select(r => r.Code).Should().Equal("Imports.OwnerLinkUnavailable");

        ImportPreconditions.Check(Mapping(customers: ["AdSoyad"], pets: petsByNumber), [ImportTable.Customers, ImportTable.Pets])
            .Select(r => r.Code).Should().Equal(["Imports.OwnerLinkUnavailable"], "müşteri tablosunda MusteriNo eşlenmemiş");

        ImportPreconditions.Check(Mapping(customers: ["MusteriNo", "AdSoyad"], pets: petsByNumber), [ImportTable.Customers, ImportTable.Pets])
            .Should().BeEmpty();

        ImportPreconditions.Check(Mapping(pets: ["SahipAdSoyad", "SahipTelefon", "HayvanAdi", "Tur"]), tables)
            .Should().BeEmpty("sahip adı+telefon ile bağlanabilir");
    }

    [Fact]
    public void Preconditions_Vaccinations_Linked_Only_By_Pet_Number_Need_Pet_Table_With_Number()
    {
        var byNumber = new[] { "HayvanNo", "AsiAdi", "UygulamaTarihi" };

        ImportPreconditions.Check(Mapping(vaccinations: byNumber), [ImportTable.Vaccinations])
            .Select(r => r.Code).Should().Equal("Imports.PetLinkUnavailable");

        ImportPreconditions.Check(
                Mapping(pets: ["SahipAdSoyad", "SahipTelefon", "HayvanAdi", "Tur"], vaccinations: byNumber),
                [ImportTable.Pets, ImportTable.Vaccinations])
            .Select(r => r.Code).Should().Equal(["Imports.PetLinkUnavailable"], "hasta tablosunda HayvanNo eşlenmemiş");

        ImportPreconditions.Check(
                Mapping(
                    pets: ["MusteriNo", "SahipAdSoyad", "SahipTelefon", "HayvanNo", "HayvanAdi", "Tur"],
                    vaccinations: byNumber),
                [ImportTable.Pets, ImportTable.Vaccinations])
            .Should().BeEmpty();
    }

    [Fact]
    public void Lookup_Keys_Should_Collect_Distinct_Normalized_Phones_Emails_Names_And_Chips()
    {
        var customers = Customers(
            Customer(name: "Ayşe  Yılmaz", phone: "0532 123 45 67", email: "A@X.com"),
            Customer(name: "Ayşe Yılmaz", phone: "+90 532 123 45 67", email: "a@x.com"),
            Customer(name: "Veli", phone: "0212 123 45 67", email: "bozuk"));
        var pets = Pets(Pet(owner: "Ali Veli", ownerPhone: "05331234567", chip: "985 112 345 678 901"));

        var keys = ImportLookupKeys.From(
            new Dictionary<ImportTable, IReadOnlyList<ImportSourceRow>>
            {
                [ImportTable.Customers] = customers,
                [ImportTable.Pets] = pets,
            },
            Mapping(CustomerFields, PetFields));

        keys.Phones.Should().BeEquivalentTo("905321234567", "905331234567");
        keys.Emails.Should().BeEquivalentTo("a@x.com");
        keys.Names.Should().BeEquivalentTo("Ayşe Yılmaz", "Veli", "Ali Veli");
        keys.Microchips.Should().BeEquivalentTo("985112345678901");
    }
}
