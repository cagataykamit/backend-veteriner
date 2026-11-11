using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Imports;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Imports;

/// <summary>Tam geçiş (ADR-048): tablo sayaçları, salt okunur eski program kayıtları, devir ücreti ve içe aktarılan randevu.</summary>
public sealed class ImportFullMigrationDomainTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly Guid BatchId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);
    private static readonly string Hash = new('a', ImportBatch.ContentHashLength);

    private static ImportBatch NewBatch()
        => ImportBatch.Create(TenantId, ClinicId, Guid.NewGuid(), "Tam geçiş", Guid.NewGuid(), Now).Value!;

    // ---------------------------------------------------------------- Sayaçlar

    [Fact]
    public void AttachFile_Should_Set_Totals_For_All_Tables()
    {
        var batch = NewBatch();

        batch.AttachFile(Hash, new Dictionary<ImportTable, int>
        {
            [ImportTable.Customers] = 2,
            [ImportTable.ClinicalRecords] = 5,
            [ImportTable.Products] = 7,
        }).IsSuccess.Should().BeTrue();

        batch.CountsOf(ImportTable.Customers).Total.Should().Be(2);
        batch.CountsOf(ImportTable.ClinicalRecords).Total.Should().Be(5);
        batch.CountsOf(ImportTable.Products).Total.Should().Be(7);
        batch.CountsOf(ImportTable.Appointments).Total.Should().Be(0);
        batch.SumCounts(c => c.Total).Should().Be(14);
    }

    [Fact]
    public void CompleteValidation_Should_Keep_Only_Total_For_Tables_Not_Given()
    {
        var batch = NewBatch();
        batch.AttachFile(Hash, new Dictionary<ImportTable, int> { [ImportTable.AccountEntries] = 4, [ImportTable.Customers] = 1 });
        batch.ApplyMapping("{}", null, Now);
        batch.StartValidation(Now);

        batch.CompleteValidation(
            Guid.NewGuid(),
            new Dictionary<ImportTable, ImportTableCounts> { [ImportTable.Customers] = new() { Total = 1, Valid = 1, WillCreate = 1 } },
            Now).IsSuccess.Should().BeTrue();

        batch.CustomerCounts.WillCreate.Should().Be(1);
        batch.AccountEntryCounts.Should().Be(ImportTableCounts.Empty with { Total = 4 });
    }

    [Fact]
    public void Negative_Row_Count_Should_Be_Rejected()
        => NewBatch().AttachFile(Hash, new Dictionary<ImportTable, int> { [ImportTable.Appointments] = -1 }).IsSuccess.Should().BeFalse();

    [Fact]
    public void Clinic_Scoped_Tables_Should_Not_Include_Customers_Pets_Products()
    {
        ImportTables.ClinicScoped.Should().BeEquivalentTo(
            [ImportTable.Vaccinations, ImportTable.ClinicalRecords, ImportTable.AccountEntries, ImportTable.Appointments]);
        ImportTables.All.Should().HaveCount(7).And.BeInAscendingOrder();
    }

    // ---------------------------------------------------------------- Klinik geçmiş

    private static ImportedClinicalRecord.Content Content(string? diagnosis = "Gastroenterit", decimal? temperature = null)
        => new("Muayene", " Kusma ", null, null, diagnosis, null, null, null, null, "Dr. Örnek", 4.5m, temperature);

    [Fact]
    public void Clinical_Record_Should_Trim_And_Keep_Content()
    {
        var created = ImportedClinicalRecord.Create(
            TenantId, ClinicId, Guid.NewGuid(), Guid.NewGuid(), BatchId, new DateOnly(2024, 5, 12), Content(), Now);

        created.IsSuccess.Should().BeTrue();
        created.Value!.Complaint.Should().Be("Kusma");
        created.Value.Diagnosis.Should().Be("Gastroenterit");
        created.Value.ImportBatchId.Should().Be(BatchId);
    }

    [Fact]
    public void Clinical_Record_Without_Content_Should_Fail()
    {
        var empty = new ImportedClinicalRecord.Content("Muayene", " ", null, null, null, null, null, null, null, "Dr.", null, null);

        ImportedClinicalRecord.Create(TenantId, ClinicId, Guid.NewGuid(), Guid.NewGuid(), BatchId, new DateOnly(2024, 1, 1), empty, Now)
            .IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Clinical_Record_Out_Of_Range_Temperature_Or_Missing_Batch_Should_Fail()
    {
        ImportedClinicalRecord.Create(
                TenantId, ClinicId, Guid.NewGuid(), Guid.NewGuid(), BatchId, new DateOnly(2024, 1, 1), Content(temperature: 50m), Now)
            .IsSuccess.Should().BeFalse();
        ImportedClinicalRecord.Create(
                TenantId, ClinicId, Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, new DateOnly(2024, 1, 1), Content(), Now)
            .IsSuccess.Should().BeFalse();
    }

    // ---------------------------------------------------------------- Hesap hareketi

    [Fact]
    public void Account_Entry_Should_Require_Positive_Amount_With_Two_Decimals()
    {
        ImportedAccountEntry.Create(
                TenantId, ClinicId, Guid.NewGuid(), BatchId, new DateOnly(2025, 3, 5), ImportedAccountEntryKind.Credit, 350.25m,
                "Aşı", "Nakit", "A-1", "Pamuk", Now)
            .IsSuccess.Should().BeTrue();

        foreach (var amount in new[] { 0m, -1m, 1.005m, 10_000_000m })
        {
            ImportedAccountEntry.Create(
                    TenantId, ClinicId, Guid.NewGuid(), BatchId, new DateOnly(2025, 3, 5), ImportedAccountEntryKind.Debit, amount,
                    null, null, null, null, Now)
                .IsSuccess.Should().BeFalse();
        }
    }

    // ---------------------------------------------------------------- Devir ücreti ve randevu

    [Fact]
    public void Opening_Balance_Charge_Should_Be_Marked_Imported()
    {
        var created = Charge.CreateOpeningBalance(
            TenantId, ClinicId, Guid.NewGuid(), "Devir bakiyesi (eski program)", 1250.50m, Guid.NewGuid(), Now, Guid.NewGuid(), BatchId);

        created.IsSuccess.Should().BeTrue();
        var charge = created.Value!;
        charge.IsImported.Should().BeTrue();
        charge.ImportBatchId.Should().Be(BatchId);
        charge.Amount.Should().Be(1250.50m);
        charge.Quantity.Should().Be(1);
        charge.PetId.Should().BeNull();
        charge.LineKind.Should().Be(ChargeLineKind.Service);
    }

    [Fact]
    public void Opening_Balance_Must_Be_Positive()
        => Charge.CreateOpeningBalance(TenantId, ClinicId, Guid.NewGuid(), "Devir", 0m, Guid.NewGuid(), Now, Guid.NewGuid(), BatchId)
            .IsSuccess.Should().BeFalse();

    [Fact]
    public void Imported_Appointment_Should_Be_Scheduled_And_Marked()
    {
        var appointment = Appointment.CreateImported(
            TenantId, ClinicId, Guid.NewGuid(), Now.AddDays(10), 45, AppointmentType.Checkup, "Hekim: Dr. Yok", null, null, BatchId);

        appointment.Status.Should().Be(AppointmentStatus.Scheduled);
        appointment.ImportBatchId.Should().Be(BatchId);
        appointment.DurationMinutes.Should().Be(45);
    }
}
