using Backend.Veteriner.Application.Imports.Commit;
using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Domain.Imports;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Imports;

/// <summary>B4 uygulama planı: sahip birimleri, mükerrer satırın kanonik birimi, hatalı satırların atlanması, yeniden çalıştırma.</summary>
public sealed class ImportCommitPlannerTests
{
    private static readonly Guid Species = Guid.NewGuid();
    private static readonly Guid Vaccine = Guid.NewGuid();

    private static ImportCommitRowInfo Row(
        ImportTable table, int number, ImportNormalizedRow? data,
        ImportRowValidationStatus validation = ImportRowValidationStatus.Valid,
        ImportRowApplyStatus apply = ImportRowApplyStatus.Pending)
        => new(Guid.NewGuid(), table, number, validation, apply, data is null ? null : ImportNormalizedJson.Serialize(data));

    private static ImportNormalizedCustomer Customer(ImportRowAction? action, Guid? target = null, int? merged = null)
        => new ImportNormalizedCustomer("M", "Ayşe Test", "905321234567", null, null) { Action = action, TargetId = target, MergedIntoRow = merged };

    private static ImportNormalizedPet Pet(string ownerKey, ImportRowAction? action, Guid? target = null, int? merged = null)
        => new ImportNormalizedPet(ownerKey, null, "Pamuk", Species, null, null, null, null, false, null, null, null, null, null)
        { Action = action, TargetId = target, MergedIntoRow = merged };

    private static ImportNormalizedVaccination Vacc(string petKey, string ownerKey, ImportRowAction? action)
        => new ImportNormalizedVaccination(petKey, ownerKey, false, Vaccine, "Kuduz", null, null, DateTime.UtcNow.AddYears(-1), null, null)
        { Action = action };

    [Fact]
    public void Build_Should_Group_Owner_With_Pets_Vaccinations_And_In_File_Duplicates()
    {
        var existing = Guid.NewGuid();
        var c2 = Row(ImportTable.Customers, 2, Customer(ImportRowAction.Create));
        var c3 = Row(ImportTable.Customers, 3, Customer(ImportRowAction.Match, existing));
        var c4 = Row(ImportTable.Customers, 4, Customer(ImportRowAction.Skip, merged: 2)); // 2. satırın mükerreri
        var p2 = Row(ImportTable.Pets, 2, Pet("file:2", ImportRowAction.Create));
        var p3 = Row(ImportTable.Pets, 3, Pet($"db:{existing}", ImportRowAction.Create));
        var v2 = Row(ImportTable.Vaccinations, 2, Vacc("file:2", "file:2", ImportRowAction.Create));

        var plan = ImportCommitPlanner.Build([v2, p3, c4, c2, p2, c3]);

        plan.RowsToSkip.Should().BeEmpty();
        plan.Units.Select(u => u.OwnerKey).Should().Equal("file:2", $"db:{existing}");
        plan.Units[0].RowIds.Should().Equal(c2.Id, c4.Id, p2.Id, v2.Id);
        plan.Units[1].RowIds.Should().Equal(c3.Id, p3.Id);
    }

    [Fact]
    public void Build_Should_Skip_Error_And_Unlinked_Rows_K2()
    {
        var error = Row(ImportTable.Customers, 2, Customer(null), ImportRowValidationStatus.Error);
        var noJson = Row(ImportTable.Pets, 2, null);
        var ok = Row(ImportTable.Customers, 3, Customer(ImportRowAction.Create));

        var plan = ImportCommitPlanner.Build([error, noJson, ok]);

        plan.RowsToSkip.Should().BeEquivalentTo([error.Id, noJson.Id]);
        plan.Units.Should().ContainSingle().Which.RowIds.Should().Equal(ok.Id);
    }

    [Fact]
    public void Build_Should_Leave_Out_Already_Applied_Units_On_Rerun()
    {
        var applied = Row(ImportTable.Customers, 2, Customer(ImportRowAction.Create), apply: ImportRowApplyStatus.Created);
        var appliedPet = Row(ImportTable.Pets, 2, Pet("file:2", ImportRowAction.Create), apply: ImportRowApplyStatus.Created);
        var pending = Row(ImportTable.Customers, 3, Customer(ImportRowAction.Create));

        var plan = ImportCommitPlanner.Build([applied, appliedPet, pending]);

        plan.Units.Should().ContainSingle().Which.OwnerKey.Should().Be("file:3");
        plan.RowsToSkip.Should().BeEmpty();
    }

    [Fact]
    public void Keys_Should_Follow_Merged_Row_To_Canonical_Key()
    {
        var existing = Guid.NewGuid();
        var rows = new Dictionary<int, ImportNormalizedCustomer>
        {
            [2] = Customer(ImportRowAction.Match, existing),
            [5] = Customer(ImportRowAction.Skip, merged: 2),
        };

        ImportCommitKeys.Customer(5, rows[5], n => rows.GetValueOrDefault(n)).Should().Be($"db:{existing}");
        ImportCommitKeys.Customer(2, Customer(ImportRowAction.Create), _ => null).Should().Be("file:2");
        ImportCommitKeys.Pet(4, Pet("file:2", ImportRowAction.Skip), _ => null).Should().BeNull("bilinçli atlanan hayvanın kaydı yok");
        ImportCommitKeys.TryParseDb($"db:{existing}", out var id).Should().BeTrue();
        id.Should().Be(existing);
        ImportCommitKeys.TryParseDb("file:2", out _).Should().BeFalse();
    }
}
