using Backend.Veteriner.Application.Imports.Commit;
using Backend.Veteriner.Application.Imports.Rollback;
using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Products;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Imports;

/// <summary>
/// Tam geçiş (ADR-048, IMPORT_API_CONTRACT.md §12.5, §12.9): uygulama planında yeni tabloların birimi, geri almada partinin
/// kendi kayıtlarının bağ sayılmaması ve yeni neden kodları. Saf fonksiyonlar; sentetik kimlikler.
/// </summary>
public sealed class ImportFullMigrationCommitRollbackTests
{
    private static ImportCommitRowInfo Row(
        ImportTable table, int number, ImportNormalizedRow? data,
        ImportRowValidationStatus validation = ImportRowValidationStatus.Valid)
        => new(Guid.NewGuid(), table, number, validation, ImportRowApplyStatus.Pending,
            data is null ? null : ImportNormalizedJson.Serialize(data));

    private static ImportNormalizedProduct Product(ImportRowAction action, Guid? target = null, int? merged = null)
        => new("Mama 1 kg", ProductKind.Other, null, "Adet", 100m, null, null, null, null)
        { Action = action, TargetId = target, MergedIntoRow = merged };

    // ---------------------------------------------------------------- Uygulama planı

    [Fact]
    public void Build_Should_Put_History_Rows_In_Owner_Unit_And_Products_In_Own_Units_After_Owners()
    {
        var customer = Row(ImportTable.Customers, 2,
            new ImportNormalizedCustomer("M", "Ayşe Test", "905321234567", null, null, 1250m) { Action = ImportRowAction.Create });
        var record = Row(ImportTable.ClinicalRecords, 2,
            new ImportNormalizedClinicalRecord("file:5", "file:2", new DateOnly(2024, 3, 1), null, "Kusma", null, null, null, null, null, null, null, null, null, null)
            { Action = ImportRowAction.Create });
        var entry = Row(ImportTable.AccountEntries, 2,
            new ImportNormalizedAccountEntry("file:2", new DateOnly(2024, 3, 1), ImportedAccountEntryKind.Debit, 500m, null, null, null, null)
            { Action = ImportRowAction.Create });
        var appointment = Row(ImportTable.Appointments, 2,
            new ImportNormalizedAppointment("file:5", "file:2", false, DateTime.UtcNow.AddDays(3), 30, AppointmentType.Checkup, null, null, null)
            { Action = ImportRowAction.Create });
        var p2 = Row(ImportTable.Products, 2, Product(ImportRowAction.Create));
        var p3 = Row(ImportTable.Products, 3, Product(ImportRowAction.Skip, merged: 2));
        var p4 = Row(ImportTable.Products, 4, Product(ImportRowAction.Match, Guid.NewGuid()));

        var plan = ImportCommitPlanner.Build([p4, p3, p2, appointment, entry, record, customer]);

        plan.RowsToSkip.Should().BeEmpty();
        plan.Units.Select(u => u.OwnerKey).Should().Equal("file:2", "product:2", "product:4");
        plan.Units[0].RowIds.Should().Equal(customer.Id, record.Id, entry.Id, appointment.Id);
        plan.Units[1].RowIds.Should().Equal(p2.Id, p3.Id);
        plan.Units[2].RowIds.Should().Equal(p4.Id);
        ImportCommitKeys.IsProductUnit("product:2").Should().BeTrue();
        ImportCommitKeys.IsProductUnit("file:2").Should().BeFalse();
    }

    [Fact]
    public void Build_Should_Skip_Error_Rows_Of_New_Tables()
    {
        var error = Row(ImportTable.AccountEntries, 2,
            new ImportNormalizedAccountEntry(null, null, null, null, null, null, null, null), ImportRowValidationStatus.Error);
        var product = Row(ImportTable.Products, 2, Product(ImportRowAction.Create), ImportRowValidationStatus.Error);

        var plan = ImportCommitPlanner.Build([error, product]);

        plan.RowsToSkip.Should().BeEquivalentTo([error.Id, product.Id]);
        plan.Units.Should().BeEmpty();
    }

    // ---------------------------------------------------------------- Geri alma

    private static readonly Guid Client = Guid.NewGuid();
    private static readonly Guid Pet = Guid.NewGuid();
    private static readonly Guid Record = Guid.NewGuid();
    private static readonly Guid OtherRecord = Guid.NewGuid();
    private static readonly Guid Entry = Guid.NewGuid();
    private static readonly Guid OtherEntry = Guid.NewGuid();
    private static readonly Guid Appointment = Guid.NewGuid();
    private static readonly Guid ProductId = Guid.NewGuid();
    private static readonly Guid Charge = Guid.NewGuid();

    private static ImportRollbackRowInfo Target(ImportTable table, int number, Guid id)
        => new(Guid.NewGuid(), table, number, ImportRowApplyStatus.Created, id);

    private static List<ImportRollbackRowInfo> Rows() =>
    [
        Target(ImportTable.Customers, 2, Client),
        Target(ImportTable.Pets, 2, Pet),
        Target(ImportTable.ClinicalRecords, 2, Record),
        Target(ImportTable.AccountEntries, 2, Entry),
        Target(ImportTable.Appointments, 2, Appointment),
        Target(ImportTable.Products, 2, ProductId),
    ];

    private static ImportRollbackFacts Facts(
        bool appointmentChanged = false,
        bool otherRecord = false,
        bool otherEntry = false,
        bool balanceChanged = false,
        IReadOnlySet<string>? productReasons = null)
        => new(
            new Dictionary<Guid, ImportRollbackVaccinationFact>(),
            new Dictionary<Guid, Guid> { [Pet] = Client },
            new Dictionary<Guid, IReadOnlyList<Guid>>(),
            new Dictionary<Guid, IReadOnlySet<string>>(),
            new Dictionary<Guid, bool> { [Client] = false },
            new Dictionary<Guid, IReadOnlyList<Guid>> { [Client] = [Pet] },
            new Dictionary<Guid, IReadOnlySet<string>>())
        {
            ClinicalRecords = new Dictionary<Guid, Guid> { [Record] = Pet },
            PetClinicalRecords = new Dictionary<Guid, IReadOnlyList<Guid>> { [Pet] = otherRecord ? [Record, OtherRecord] : [Record] },
            AccountEntries = new Dictionary<Guid, Guid> { [Entry] = Client },
            ClientAccountEntries = new Dictionary<Guid, IReadOnlyList<Guid>> { [Client] = otherEntry ? [Entry, OtherEntry] : [Entry] },
            Appointments = new Dictionary<Guid, ImportRollbackAppointmentFact>
            {
                [Appointment] = new(Pet, appointmentChanged, HasVisits: false, HasExaminations: false),
            },
            OpeningBalances = new Dictionary<Guid, ImportRollbackOpeningBalanceFact> { [Client] = new(Charge, balanceChanged) },
            Products = new Dictionary<Guid, IReadOnlySet<string>> { [ProductId] = productReasons ?? new HashSet<string>() },
        };

    [Fact]
    public void Batch_Own_Records_Do_Not_Block_And_Everything_Is_Deleted_With_Opening_Balance()
    {
        var plan = ImportRollbackPlanner.Build(Rows(), Facts());

        plan.HasBlocked.Should().BeFalse();
        plan.Items.Should().OnlyContain(i => i.Decision == ImportRollbackDecision.Delete);
        plan.CountOpeningBalances(ImportRollbackDecision.Delete).Should().Be(1);
        plan.OpeningBalances[Client].Should().Be(Charge);

        // Sahip birimi müşteri + hasta + geçmiş + randevu + hesap hareketi; ürün kendi biriminde, sonda.
        plan.Units.Select(u => u.OwnerKey).Should().Equal(Client, ProductId);
        plan.Units[0].RowIds.Should().HaveCount(5);
    }

    [Fact]
    public void Changed_Appointment_Is_Kept_And_Keeps_Pet_And_Client()
    {
        var plan = ImportRollbackPlanner.Build(Rows(), Facts(appointmentChanged: true));

        Reasons(plan, ImportTable.Appointments).Should().Equal(ImportRollbackReasons.AppointmentChanged);
        Reasons(plan, ImportTable.Pets).Should().Contain(ImportRollbackReasons.HasKeptAppointments);
        Reasons(plan, ImportTable.Customers).Should().Contain(ImportRollbackReasons.HasKeptPets);
        Decision(plan, ImportTable.ClinicalRecords).Should().Be(ImportRollbackDecision.Delete);
        plan.CountOpeningBalances(ImportRollbackDecision.Keep).Should().Be(1);
    }

    [Fact]
    public void Other_Batch_History_And_Changed_Opening_Balance_Block()
    {
        var plan = ImportRollbackPlanner.Build(Rows(), Facts(otherRecord: true, otherEntry: true, balanceChanged: true));

        Reasons(plan, ImportTable.Pets).Should().Equal(ImportRollbackReasons.HasOtherClinicalRecords);
        Reasons(plan, ImportTable.Customers).Should().BeEquivalentTo(
            [ImportRollbackReasons.HasKeptPets, ImportRollbackReasons.HasOtherAccountEntries, ImportRollbackReasons.OpeningBalanceChanged]);
    }

    [Fact]
    public void Product_Reasons_Come_From_Dependencies_And_Gone_Records_Are_RolledBack()
    {
        var facts = Facts(productReasons: new HashSet<string> { ImportRollbackReasons.HasStock, ImportRollbackReasons.ProductModified }) with
        {
            ClinicalRecords = new Dictionary<Guid, Guid>(),
        };

        var plan = ImportRollbackPlanner.Build(Rows(), facts);

        Reasons(plan, ImportTable.Products).Should().Equal(ImportRollbackReasons.HasStock, ImportRollbackReasons.ProductModified);
        Decision(plan, ImportTable.ClinicalRecords).Should().Be(ImportRollbackDecision.Gone);
        Decision(plan, ImportTable.Customers).Should().Be(ImportRollbackDecision.Delete);
    }

    [Fact]
    public void Fail_Policy_Keeps_Whole_Unit_Including_Read_Only_Records()
    {
        var plan = ImportRollbackPlanner.KeepWholeUnitIfBlocked(ImportRollbackPlanner.Build(Rows(), Facts(appointmentChanged: true)));

        Reasons(plan, ImportTable.ClinicalRecords).Should().Equal(ImportRollbackReasons.OwnerUnitBlocked);
        Reasons(plan, ImportTable.AccountEntries).Should().Equal(ImportRollbackReasons.OwnerUnitBlocked);
        plan.OpeningBalances.Should().ContainKey(Client);
    }

    [Fact]
    public void Targets_Carry_Batch_And_New_Table_Ids()
    {
        var batchId = Guid.NewGuid();

        var targets = ImportRollbackPlanner.Targets(batchId, Rows());

        targets.BatchId.Should().Be(batchId);
        targets.ClinicalRecordIds.Should().Equal(Record);
        targets.AccountEntryIds.Should().Equal(Entry);
        targets.AppointmentIds.Should().Equal(Appointment);
        targets.ProductIds.Should().Equal(ProductId);
        ImportRollbackPlanner.EntityOf(ImportTable.Products).Should().Be(ImportRollbackPlanner.ProductEntity);
    }

    private static IReadOnlyList<string> Reasons(ImportRollbackPlan plan, ImportTable table)
        => plan.Items.Single(i => i.Row.Table == table).Reasons;

    private static ImportRollbackDecision Decision(ImportRollbackPlan plan, ImportTable table)
        => plan.Items.Single(i => i.Row.Table == table).Decision;
}
