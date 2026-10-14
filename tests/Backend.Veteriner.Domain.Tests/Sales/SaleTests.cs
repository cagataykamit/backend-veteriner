using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Sales;
using FluentAssertions;

namespace Backend.Veteriner.Domain.Tests.Sales;

public sealed class SaleTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);

    private static SaleLineDraft Line(decimal quantity = 1m, decimal unitPrice = 10m, Guid? productId = null)
        => new(productId ?? Guid.NewGuid(), "Mama", "Adet", quantity, unitPrice);

    private static Domain.Shared.Result<Sale> Create(
        IReadOnlyList<SaleLineDraft> lines, string? notes = null, Guid? requestId = null, string hash = "H")
        => Sale.Create(TenantId, ClinicId, ClientId, Now, notes, lines, UserId, Now, requestId ?? Guid.NewGuid(), hash);

    [Fact]
    public void Create_Should_Compute_Line_Amounts_Round_Half_Away_And_Total()
    {
        // 0.333 × 10.05 = 3.34665 → 3.35; 2 × 4.50 = 9.00
        var result = Create([Line(0.333m, 10.05m), Line(2m, 4.50m)]);

        result.IsSuccess.Should().BeTrue();
        var sale = result.Value!;
        sale.Lines.Select(l => l.LineAmount).Should().Equal(3.35m, 9.00m);
        sale.TotalAmount.Should().Be(12.35m);
        sale.IsVoided.Should().BeFalse();
        sale.Lines.Should().OnlyContain(l => l.SaleId == sale.Id);
    }

    [Fact]
    public void Create_Should_Copy_Product_Snapshot_And_Trim_Notes()
    {
        var sale = Create([Line()], "  teslim edildi  ").Value!;

        sale.Notes.Should().Be("teslim edildi");
        sale.Lines[0].ProductName.Should().Be("Mama");
        sale.Lines[0].Unit.Should().Be("Adet");
        sale.SoldAtUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100_000.001)]
    [InlineData(1.0005)]
    public void Create_Should_Reject_Invalid_Quantity(double quantity)
    {
        var result = Create([Line((decimal)quantity)]);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Sales.Validation");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10_000_000.01)]
    [InlineData(1.005)]
    public void Create_Should_Reject_Invalid_UnitPrice(double unitPrice)
    {
        var result = Create([Line(1m, (decimal)unitPrice)]);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Sales.Validation");
    }

    [Fact]
    public void Create_Should_Reject_Line_That_Rounds_To_Zero()
    {
        var result = Create([Line(0.001m, 0.01m)]);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Sales.Validation");
    }

    [Fact]
    public void Create_Should_Reject_Empty_And_Too_Many_Lines()
    {
        Create([]).Error.Code.Should().Be("Sales.Validation");
        Create(Enumerable.Range(0, Sale.MaxLines + 1).Select(_ => Line()).ToList()).Error.Code.Should().Be("Sales.Validation");
        Create(Enumerable.Range(0, Sale.MaxLines).Select(_ => Line()).ToList()).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_Should_Reject_Total_Above_Charge_Limit()
    {
        var result = Create([Line(2m, 6_000_000m)]);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Sales.Validation");
    }

    [Fact]
    public void Create_Should_Require_RequestId_And_Hash_And_Long_Notes_Rejected()
    {
        Create([Line()], requestId: Guid.Empty).Error.Code.Should().Be("Sales.RequestIdRequired");
        Create([Line()], hash: " ").Error.Code.Should().Be("Sales.Validation");
        Create([Line()], notes: new string('x', Sale.MaxNotesLength + 1)).Error.Code.Should().Be("Sales.Validation");
    }

    [Fact]
    public void Void_Should_Require_Reason_And_Be_Once_Only()
    {
        var sale = Create([Line()]).Value!;

        sale.Void(UserId, "abc", SaleVoidStockOutcome.StockReturned, Now).Error.Code.Should().Be("Sales.Validation");
        sale.IsVoided.Should().BeFalse();

        var voided = sale.Void(UserId, "  yanlis urun  ", SaleVoidStockOutcome.WrittenOff, Now);

        voided.IsSuccess.Should().BeTrue();
        sale.IsVoided.Should().BeTrue();
        sale.VoidReason.Should().Be("yanlis urun");
        sale.VoidStockOutcome.Should().Be(SaleVoidStockOutcome.WrittenOff);
        sale.Void(UserId, "yanlis urun", SaleVoidStockOutcome.StockReturned, Now).Error.Code.Should().Be("Sales.AlreadyVoided");
    }

    [Fact]
    public void AttachFinance_Should_Set_Charge_And_Optional_Payment()
    {
        var sale = Create([Line()]).Value!;
        var chargeId = Guid.NewGuid();

        sale.AttachFinance(chargeId, null);

        sale.ChargeId.Should().Be(chargeId);
        sale.PaymentId.Should().BeNull();
    }

    [Fact]
    public void WalkIn_Client_Should_Be_Flagged_With_Fixed_Name()
    {
        var walkIn = Client.CreateWalkIn(TenantId);

        walkIn.IsWalkIn.Should().BeTrue();
        walkIn.FullName.Should().Be(Client.WalkInFullName);
        new Client(TenantId, "Ayse Yilmaz").IsWalkIn.Should().BeFalse();
    }
}
