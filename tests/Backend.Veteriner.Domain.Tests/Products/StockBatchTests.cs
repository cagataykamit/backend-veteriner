using Backend.Veteriner.Domain.Products;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Products;

public sealed class StockBatchTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicId = Guid.NewGuid();

    private static Product Plain() => new(TenantId, "Mama", "Adet", 1m, "TRY", Now);

    private static Product Vaccine() => new(TenantId, "Kuduz", "Doz", 1m, "TRY", Now, kind: ProductKind.Vaccine);

    private static Product Narcotic()
        => new(TenantId, "Ketamin", "Şişe", 1m, "TRY", Now, controlledClass: ControlledSubstanceClass.Narcotic);

    private static StockBatch NewBatch(Product p, decimal qty, DateOnly? expiry = null, DateTime? createdAt = null)
        => StockBatch.Create(
            TenantId, ClinicId, p, qty, new StockBatchDetails(LotNumber: "L1", ExpiryDate: expiry),
            null, createdAt ?? DateTime.UtcNow).Value!;

    [Fact]
    public void Product_Defaults_Should_Be_Other_And_NotControlled_And_NoLotRequirement()
    {
        var p = Plain();

        p.Kind.Should().Be(ProductKind.Other);
        p.ControlledClass.Should().Be(ControlledSubstanceClass.None);
        p.RequiresLotAndExpiry.Should().BeFalse();
    }

    [Fact]
    public void Product_Should_Require_Lot_For_Vaccine_And_For_Controlled_But_Not_Plain_Medicine()
    {
        Vaccine().RequiresLotAndExpiry.Should().BeTrue();
        Narcotic().RequiresLotAndExpiry.Should().BeTrue();
        new Product(TenantId, "İlaç", "Kutu", 1m, "TRY", Now, kind: ProductKind.Medicine).RequiresLotAndExpiry.Should().BeFalse();
    }

    [Fact]
    public void Product_Update_Should_Change_Class_And_Normalize_Forms()
    {
        var p = Plain();

        p.Update(null, "Mama", "Adet", 1m, "TRY", Now, kind: ProductKind.Medicine,
            controlledClass: ControlledSubstanceClass.Psychotropic, pharmacologicalForm: "  tablet ",
            presentationForm: " ", packaging: "10 lu blister");

        p.Kind.Should().Be(ProductKind.Medicine);
        p.ControlledClass.Should().Be(ControlledSubstanceClass.Psychotropic);
        p.PharmacologicalForm.Should().Be("tablet");
        p.PresentationForm.Should().BeNull();
        p.Packaging.Should().Be("10 lu blister");
    }

    [Fact]
    public void Create_Should_Set_Remaining_To_Initial_And_Normalize_Text()
    {
        var details = new StockBatchDetails(
            LotNumber: "  A-1 ", ExpiryDate: new DateOnly(2027, 5, 1), SupplierName: " Firma ", InvoiceNumber: "F-9",
            InvoiceDate: new DateOnly(2026, 10, 1), ReceivedDate: new DateOnly(2026, 10, 3),
            DispatchedDate: new DateOnly(2026, 10, 2), TransportMethod: "Soğuk zincir", ColdChainMonitor: "Logger");

        var result = StockBatch.Create(TenantId, ClinicId, Plain(), 10m, details, Guid.NewGuid(), DateTime.UtcNow);

        result.IsSuccess.Should().BeTrue();
        var b = result.Value!;
        b.InitialQuantity.Should().Be(10m);
        b.RemainingQuantity.Should().Be(10m);
        b.LotNumber.Should().Be("A-1");
        b.SupplierName.Should().Be("Firma");
        b.ExpiryDate.Should().Be(new DateOnly(2027, 5, 1));
        b.ColdChainMonitor.Should().Be("Logger");
    }

    [Fact]
    public void Create_Should_Allow_Missing_Lot_And_Expiry_For_Plain_Product()
        => StockBatch.Create(TenantId, ClinicId, Plain(), 1m, new StockBatchDetails(SupplierName: "F"), null, DateTime.UtcNow)
            .IsSuccess.Should().BeTrue();

    [Theory]
    [InlineData(null, "2027-01-01")]
    [InlineData("  ", "2027-01-01")]
    [InlineData("L1", null)]
    public void Create_Should_Require_Lot_And_Expiry_For_Vaccine_And_Controlled(string? lot, string? expiry)
    {
        var details = new StockBatchDetails(LotNumber: lot, ExpiryDate: expiry is null ? null : DateOnly.Parse(expiry));

        foreach (var product in new[] { Vaccine(), Narcotic() })
        {
            var result = StockBatch.Create(TenantId, ClinicId, product, 1m, details, null, DateTime.UtcNow);

            result.IsSuccess.Should().BeFalse();
            result.Error.Code.Should().Be(StockBatch.LotAndExpiryRequiredCode);
        }
    }

    [Fact]
    public void Create_Should_Reject_NonPositive_Quantity_And_DispatchAfterReceived()
    {
        StockBatch.Create(TenantId, ClinicId, Plain(), 0m, new StockBatchDetails(), null, DateTime.UtcNow)
            .Error.Code.Should().Be(StockBatch.ValidationCode);

        var late = new StockBatchDetails(ReceivedDate: new DateOnly(2026, 10, 1), DispatchedDate: new DateOnly(2026, 10, 2));
        StockBatch.Create(TenantId, ClinicId, Plain(), 1m, late, null, DateTime.UtcNow)
            .Error.Code.Should().Be(StockBatch.ValidationCode);
    }

    [Fact]
    public void IsExpiredOn_Should_Treat_Expiry_Day_As_Usable()
    {
        var b = NewBatch(Plain(), 1m, new DateOnly(2026, 10, 13));

        b.IsExpiredOn(new DateOnly(2026, 10, 13)).Should().BeFalse();
        b.IsExpiredOn(new DateOnly(2026, 10, 14)).Should().BeTrue();
        NewBatch(Plain(), 1m, expiry: null).IsExpiredOn(new DateOnly(2099, 1, 1)).Should().BeFalse();
    }

    [Fact]
    public void Decrease_Increase_SetRemaining_Should_Guard_Invariants()
    {
        var b = NewBatch(Plain(), 5m);

        b.Decrease(2m);
        b.RemainingQuantity.Should().Be(3m);
        b.Increase(1m);
        b.RemainingQuantity.Should().Be(4m);
        b.SetRemaining(0m);
        b.RemainingQuantity.Should().Be(0m);

        b.Invoking(x => x.Decrease(1m)).Should().Throw<InvalidOperationException>();
        b.Invoking(x => x.Decrease(0m)).Should().Throw<ArgumentOutOfRangeException>();
        b.Invoking(x => x.SetRemaining(-1m)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void PickFirstSufficient_Should_Order_By_Expiry_Then_Entry_And_Put_NoExpiry_Last()
    {
        var p = Plain();
        var noExpiry = NewBatch(p, 10m, null, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var late = NewBatch(p, 10m, new DateOnly(2027, 6, 1));
        var soon = NewBatch(p, 10m, new DateOnly(2027, 1, 1), new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        var soonLaterEntry = NewBatch(p, 10m, new DateOnly(2027, 1, 1), new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        StockBatch.PickFirstSufficient(new[] { noExpiry, late, soonLaterEntry, soon }, 1m, null).Should().BeSameAs(soon);
        StockBatch.PickFirstSufficient(new[] { noExpiry, late }, 1m, null).Should().BeSameAs(late);
        StockBatch.PickFirstSufficient(new[] { noExpiry }, 1m, null).Should().BeSameAs(noExpiry);
    }

    [Fact]
    public void PickFirstSufficient_Should_Pick_First_Batch_That_Covers_Quantity_Not_Just_Earliest()
    {
        var p = Plain();
        var soonButSmall = NewBatch(p, 2m, new DateOnly(2027, 1, 1));
        var laterEnough = NewBatch(p, 6m, new DateOnly(2027, 3, 1));

        StockBatch.PickFirstSufficient(new[] { soonButSmall, laterEnough }, 5m, null).Should().BeSameAs(laterEnough);
        StockBatch.PickFirstSufficient(new[] { soonButSmall, laterEnough }, 7m, null).Should().BeNull();
    }

    [Fact]
    public void PickFirstSufficient_Should_Skip_Expired_Batches_When_Day_Given()
    {
        var p = Plain();
        var expired = NewBatch(p, 10m, new DateOnly(2026, 1, 1));
        var valid = NewBatch(p, 10m, new DateOnly(2027, 1, 1));
        var today = new DateOnly(2026, 10, 13);

        StockBatch.PickFirstSufficient(new[] { expired, valid }, 1m, today).Should().BeSameAs(valid);
        StockBatch.PickFirstSufficient(new[] { expired, valid }, 1m, notExpiredOn: null).Should().BeSameAs(expired);
        StockBatch.PickFirstSufficient(new[] { expired }, 1m, today).Should().BeNull();
    }

    [Fact]
    public void StockMovement_LinkBatch_Should_Record_Batch_And_Balance_After()
    {
        var p = Plain();
        var b = NewBatch(p, 10m);
        var m = new StockMovement(TenantId, ClinicId, p.Id, StockMovementType.Out, 4m, DateTime.UtcNow, Now);

        b.Decrease(4m);
        m.LinkBatch(b);

        m.BatchId.Should().Be(b.Id);
        m.Batch.Should().BeSameAs(b);
        m.BatchBalanceAfter.Should().Be(6m);
    }
}
