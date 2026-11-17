using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Domain.Imports;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Imports;

/// <summary>
/// Güncelleme aktarması (ADR-051, IMPORT_API_CONTRACT.md §14): kaynak kimliğiyle eşleşme, bakiye farkı, kayıt türlü klinik kayıt
/// anahtarı ve önizleme özeti. Girdi bellektedir; sentetik veri.
/// </summary>
public sealed class ImportUpdateImportTests
{
    private static readonly DateTime Now = new(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Cat = Guid.NewGuid();
    private static readonly Guid Dog = Guid.NewGuid();

    private static readonly ImportCatalogs Catalogs = new(
        Species: [new(Cat, "Kedi", "CAT", null), new(Dog, "Köpek", "DOG", null)], Breeds: [], Colors: [], Vaccines: []);

    private static readonly string[] CustomerFields = ["MusteriNo", "AdSoyad", "Telefon", "Eposta", "AcikBakiye"];
    private static readonly string[] PetFields = ["MusteriNo", "HayvanNo", "HayvanAdi", "Tur"];
    private static readonly string[] ClinicalFields = ["HayvanNo", "Tarih", "KayitTuru", "Sikayet", "Tani"];

    private static readonly Dictionary<ImportTable, string[]> FieldsOf = new()
    {
        [ImportTable.Customers] = CustomerFields,
        [ImportTable.Pets] = PetFields,
        [ImportTable.ClinicalRecords] = ClinicalFields,
    };

    private static IReadOnlyList<ImportSourceRow> Table(ImportTable table, params string?[][] rows)
        => rows.Select((cells, i) => new ImportSourceRow(
                i + 2,
                FieldsOf[table].Select((h, c) => (h, v: c < cells.Length ? cells[c] : null)).ToDictionary(x => x.h, x => x.v)))
            .ToList();

    private static ImportValidationOutput Run(Dictionary<ImportTable, IReadOnlyList<ImportSourceRow>> tables, ImportExistingData existing)
    {
        var mapping = new ImportMappingDocument
        {
            Columns = tables.Keys.ToDictionary(ImportCanonicalNames.Key, t => FieldsOf[t].ToDictionary(f => f, f => f)),
        };
        return ImportValidationEngine.Run(new ImportValidationInput(
            tables, mapping, new ImportOptionsDocument(ImportOptionsDocument.Match), Catalogs, existing, Now));
    }

    private static ImportRowResult Row(ImportValidationOutput output, ImportTable table, int row = 2)
        => output.Rows.Single(r => r.Table == table && r.RowNumber == row);

    private static string[] Codes(ImportRowResult r) => r.Issues.Select(i => i.Code).ToArray();

    // ================================================================ Kaynak kimliği

    [Fact]
    public void Customer_Linked_By_Source_Id_Should_Match_Even_When_Name_And_Phone_Changed()
    {
        var clientId = Guid.NewGuid();
        var existing = new ImportExistingData([new(clientId, "Ayşe Yılmaz", "905321234567", null)], [], [])
        {
            ClientLinks = new Dictionary<string, Guid> { ["1001"] = clientId },
        };

        var output = Run(
            new() { [ImportTable.Customers] = Table(ImportTable.Customers, ["1001", "Ayşe Yılmaz Demir", "05559998877"]) },
            existing);

        var row = Row(output, ImportTable.Customers);
        row.Normalized.Action.Should().Be(ImportRowAction.Match);
        row.Normalized.TargetId.Should().Be(clientId);
        Codes(row).Should().Contain(ImportRules.MatchedBySourceId.Code).And.NotContain(ImportRules.MatchedExisting.Code);
    }

    [Fact]
    public void Customer_Without_Phone_And_Email_Should_Match_By_Source_Id_Instead_Of_Becoming_A_Duplicate()
    {
        var clientId = Guid.NewGuid();
        var existing = new ImportExistingData([new(clientId, "Mehmet Kaya", null, null)], [], [])
        {
            ClientLinks = new Dictionary<string, Guid> { ["77"] = clientId },
        };

        var output = Run(new() { [ImportTable.Customers] = Table(ImportTable.Customers, ["77", "Mehmet Kaya"]) }, existing);

        var row = Row(output, ImportTable.Customers);
        row.Normalized.Action.Should().Be(ImportRowAction.Match);
        Codes(row).Should().NotContain(ImportRules.PossibleDuplicate.Code);
    }

    [Fact]
    public void Customer_Without_Link_Should_Fall_Back_To_Name_And_Phone()
    {
        var clientId = Guid.NewGuid();
        var existing = new ImportExistingData([new(clientId, "Ayşe Yılmaz", "905321234567", null)], [], []);

        var output = Run(
            new() { [ImportTable.Customers] = Table(ImportTable.Customers, ["1001", "Ayşe Yılmaz", "05321234567"]) },
            existing);

        var row = Row(output, ImportTable.Customers);
        row.Normalized.TargetId.Should().Be(clientId);
        Codes(row).Should().Contain(ImportRules.MatchedExisting.Code);
    }

    [Fact]
    public void Pet_Linked_By_Source_Id_Should_Match_Even_When_Renamed_And_Records_Of_It_Should_Be_Deduplicated()
    {
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var existing = new ImportExistingData(
            [new(clientId, "Mehmet Kaya", null, null)],
            [new(petId, clientId, "Boncuk", Cat, null, null)],
            [])
        {
            ClientLinks = new Dictionary<string, Guid> { ["77"] = clientId },
            PetLinks = new Dictionary<string, Guid> { ["501"] = petId },
            ClinicalRecords = [new(petId, new DateOnly(2026, 11, 10), "Kusma", null, null, "Gastrit", null, null, null, null)],
        };

        var output = Run(new()
        {
            [ImportTable.Customers] = Table(ImportTable.Customers, ["77", "Mehmet Kaya"]),
            [ImportTable.Pets] = Table(ImportTable.Pets, ["77", "501", "Boncuk Hanım", "Kedi"]),
            [ImportTable.ClinicalRecords] = Table(ImportTable.ClinicalRecords,
                ["501", "10.11.2026", "Muayene", "kusma", "GASTRİT"],
                ["501", "11.11.2026", "Muayene", "Kusma", "Gastrit"],
                ["501", "15.11.2026", "Muayene", "İshal", null]),
        }, existing);

        var pet = Row(output, ImportTable.Pets);
        pet.Normalized.Action.Should().Be(ImportRowAction.Match);
        pet.Normalized.TargetId.Should().Be(petId);
        Codes(pet).Should().Contain(ImportRules.MatchedBySourceId.Code);

        // Aynı hasta + gün + içerik: daha önce aktarıldı, atlanır; gün ya da içerik farklıysa yeni kayıttır.
        Row(output, ImportTable.ClinicalRecords, 2).Normalized.Action.Should().Be(ImportRowAction.Skip);
        Codes(Row(output, ImportTable.ClinicalRecords, 2)).Should().Contain(ImportRules.ClinicalRecordImportedBefore.Code);
        Row(output, ImportTable.ClinicalRecords, 3).Normalized.Action.Should().Be(ImportRowAction.Create);
        Row(output, ImportTable.ClinicalRecords, 4).Normalized.Action.Should().Be(ImportRowAction.Create);
    }

    [Theory]
    [InlineData(ImportSourceProfile.EVet, " 1001 ", "1001")]
    [InlineData(ImportSourceProfile.Kolayvet, "A-7", "A-7")]
    [InlineData(ImportSourceProfile.Generic, "1001", null)]
    [InlineData(ImportSourceProfile.EVet, "1001#57", null)]
    [InlineData(ImportSourceProfile.EVet, "", null)]
    [InlineData(ImportSourceProfile.EVet, null, null)]
    public void Stable_Key_Should_Exist_Only_For_Program_Profiles_And_Unique_Numbers(
        ImportSourceProfile profile, string? reference, string? expected)
        => ImportSourceLink.StableKey(profile, reference).Should().Be(expected);

    [Fact]
    public void Retarget_Should_Move_The_Link_Only_When_The_Target_Changes()
    {
        var batch = Guid.NewGuid();
        var link = ImportSourceLink.Create(Guid.NewGuid(), Guid.NewGuid(), ImportSourceProfile.EVet, ImportSourceEntity.Pet, "501", Guid.NewGuid(), batch, Now);
        var target = link.TargetId;

        link.Retarget(target, Guid.NewGuid(), Now.AddDays(1));
        link.BatchId.Should().Be(batch);
        link.UpdatedAtUtc.Should().BeNull();

        var newTarget = Guid.NewGuid();
        var newBatch = Guid.NewGuid();
        link.Retarget(newTarget, newBatch, Now.AddDays(1));
        link.TargetId.Should().Be(newTarget);
        link.BatchId.Should().Be(newBatch);
        link.UpdatedAtUtc.Should().NotBeNull();
    }

    // ================================================================ Bakiye farkı

    [Theory]
    [InlineData("1.450,00", 1200.00, 250.00)]
    [InlineData(null, 300.00, -300.00)]
    [InlineData("-50", 0.00, -50.00)]
    public void Matched_Customer_With_A_Different_Balance_Should_Carry_The_Difference_Without_Applying_It(
        string? fileBalance, double current, double expectedDifference)
    {
        var clientId = Guid.NewGuid();
        var existing = new ImportExistingData([new(clientId, "Ayşe Yılmaz", "905321234567", null)], [], [])
        {
            ClientBalances = new Dictionary<Guid, decimal> { [clientId] = (decimal)current },
        };

        var output = Run(
            new() { [ImportTable.Customers] = Table(ImportTable.Customers, ["1001", "Ayşe Yılmaz", "05321234567", null, fileBalance]) },
            existing);

        var row = Row(output, ImportTable.Customers);
        var customer = (ImportNormalizedCustomer)row.Normalized;
        customer.Action.Should().Be(ImportRowAction.Match);
        customer.OpeningBalance.Should().BeNull();
        customer.ExistingBalance.Should().Be((decimal)current);
        customer.BalanceDifference.Should().Be((decimal)expectedDifference);
        Codes(row).Should().Contain(ImportRules.BalanceDifference.Code).And.NotContain(ImportRules.BalanceNotAppliedToExisting.Code);
        row.Status.Should().Be(ImportRowValidationStatus.Warning);
    }

    [Fact]
    public void Matched_Customer_With_The_Same_Balance_Should_Have_No_Difference_Or_Note()
    {
        var clientId = Guid.NewGuid();
        var existing = new ImportExistingData([new(clientId, "Ayşe Yılmaz", "905321234567", null)], [], [])
        {
            ClientBalances = new Dictionary<Guid, decimal> { [clientId] = 500m },
        };

        var output = Run(
            new() { [ImportTable.Customers] = Table(ImportTable.Customers, ["1001", "Ayşe Yılmaz", "05321234567", null, "500"]) },
            existing);

        var row = Row(output, ImportTable.Customers);
        ((ImportNormalizedCustomer)row.Normalized).BalanceDifference.Should().BeNull();
        Codes(row).Should().NotContain(ImportRules.BalanceDifference.Code).And.NotContain(ImportRules.BalanceNotAppliedToExisting.Code);
    }

    [Fact]
    public void Unreadable_Balance_Should_Not_Produce_A_Difference()
    {
        var clientId = Guid.NewGuid();
        var existing = new ImportExistingData([new(clientId, "Ayşe Yılmaz", "905321234567", null)], [], [])
        {
            ClientBalances = new Dictionary<Guid, decimal> { [clientId] = 500m },
        };

        var output = Run(
            new() { [ImportTable.Customers] = Table(ImportTable.Customers, ["1001", "Ayşe Yılmaz", "05321234567", null, "beş yüz"]) },
            existing);

        var row = Row(output, ImportTable.Customers);
        ((ImportNormalizedCustomer)row.Normalized).BalanceDifference.Should().BeNull();
        Codes(row).Should().Contain(ImportRules.AmountInvalid.Code).And.NotContain(ImportRules.BalanceDifference.Code);
    }

    // ================================================================ Önizleme

    [Fact]
    public void Overlap_Should_Count_Matches_And_Already_Imported_Rows_And_Flag_An_Update_From_Half()
    {
        var counts = new Dictionary<ImportTable, ImportTableCounts>
        {
            [ImportTable.Customers] = ImportTableCounts.Empty with { Total = 10, Valid = 10, WillCreate = 2, WillMatch = 8 },
            [ImportTable.ClinicalRecords] = ImportTableCounts.Empty with { Total = 100, Valid = 100, WillCreate = 5, WillSkip = 95 },
        };
        IReadOnlyList<ImportNotImportedReasonDto> reasons =
        [
            new("clinicalRecords", "Skipped", ImportRules.ClinicalRecordImportedBefore.Code, 90),
            new("clinicalRecords", "Skipped", ImportRules.ClinicalRecordEmpty.Code, 5),
        ];
        IReadOnlyList<ImportIssueCountDto> issueCounts = [new(ImportRules.MatchedBySourceId.Code, "Info", 7)];

        var overlap = ImportPreviewAggregator.Overlap(t => counts.GetValueOrDefault(t, ImportTableCounts.Empty), reasons, issueCounts);

        overlap.IsUpdate.Should().BeTrue();
        overlap.MatchedBySourceId.Should().Be(7);
        overlap.Tables.Should().Equal(
            new ImportOverlapTableDto("customers", 2, 8),
            new ImportOverlapTableDto("clinicalRecords", 5, 90));
    }

    [Fact]
    public void Overlap_Should_Not_Flag_A_First_Import()
    {
        var counts = ImportTableCounts.Empty with { Total = 10, Valid = 10, WillCreate = 9, WillMatch = 1 };

        var overlap = ImportPreviewAggregator.Overlap(t => t == ImportTable.Customers ? counts : ImportTableCounts.Empty, [], []);

        overlap.IsUpdate.Should().BeFalse();
        overlap.Tables.Should().ContainSingle();
    }

    [Fact]
    public void Balance_Differences_Should_Be_Listed_By_Absolute_Difference_With_Totals()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        string Json(Guid id, string name, decimal current, decimal difference) => ImportNormalizedJson.Serialize(
            new ImportNormalizedCustomer("1", name, null, null, null, ExistingBalance: current, BalanceDifference: difference)
            {
                Action = ImportRowAction.Match,
                TargetId = id,
            });

        var result = ImportPreviewAggregator.BalanceDifferences(
        [
            (5, Json(a, "Ayşe", 100m, 50m)),
            (9, Json(b, "Mehmet", 400m, -300m)),
        ]);

        result.Count.Should().Be(2);
        result.TotalIncrease.Should().Be(50m);
        result.TotalDecrease.Should().Be(300m);
        result.Truncated.Should().BeFalse();
        result.Items.Select(i => i.RowNumber).Should().Equal(9, 5);
        result.Items[0].Should().Be(new ImportBalanceDifferenceDto(9, b, "Mehmet", 400m, 100m, -300m));
    }
}
