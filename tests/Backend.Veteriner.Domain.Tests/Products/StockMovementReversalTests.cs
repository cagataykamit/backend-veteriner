using Backend.Veteriner.Domain.Products;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Products;

public sealed class StockMovementReversalTests
{
    private static StockMovement Movement(StockMovementType type)
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), type, 4m, DateTime.UtcNow,
            unitCost: 2m, referenceType: " Administration ", referenceId: Guid.NewGuid());

    [Theory]
    [InlineData(StockMovementType.Initial, true)]
    [InlineData(StockMovementType.In, true)]
    [InlineData(StockMovementType.Out, true)]
    [InlineData(StockMovementType.Adjustment, false)]
    [InlineData(StockMovementType.Reversal, false)]
    public void IsReversible_Should_Follow_MovementType(StockMovementType type, bool expected)
        => Movement(type).IsReversible.Should().Be(expected);

    [Fact]
    public void ReversalIncreasesStock_Should_BeTrue_Only_For_Out()
    {
        Movement(StockMovementType.Out).ReversalIncreasesStock.Should().BeTrue();
        Movement(StockMovementType.In).ReversalIncreasesStock.Should().BeFalse();
        Movement(StockMovementType.Initial).ReversalIncreasesStock.Should().BeFalse();
    }

    [Fact]
    public void CreateReversal_Should_Copy_Scope_Quantity_And_Reference_And_Link_Original()
    {
        var original = Movement(StockMovementType.Out);
        var requestId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var reversal = original.CreateReversal("  Yanlış ürün ", "not", DateTime.UtcNow, userId, requestId);

        reversal.MovementType.Should().Be(StockMovementType.Reversal);
        reversal.ReversesMovementId.Should().Be(original.Id);
        reversal.TenantId.Should().Be(original.TenantId);
        reversal.ClinicId.Should().Be(original.ClinicId);
        reversal.ProductId.Should().Be(original.ProductId);
        reversal.Quantity.Should().Be(4m);
        reversal.UnitCost.Should().Be(2m);
        reversal.ReferenceType.Should().Be("Administration");
        reversal.ReferenceId.Should().Be(original.ReferenceId);
        reversal.Reason.Should().Be("Yanlış ürün");
        reversal.CreatedByUserId.Should().Be(userId);
        reversal.RequestId.Should().Be(requestId);
    }

    [Theory]
    [InlineData(StockMovementType.Adjustment)]
    [InlineData(StockMovementType.Reversal)]
    public void CreateReversal_Should_Throw_When_NotReversible(StockMovementType type)
    {
        var act = () => Movement(type).CreateReversal("x", null, DateTime.UtcNow, null, null);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CreateReversal_Should_Throw_When_Reason_Blank()
    {
        var act = () => Movement(StockMovementType.Out).CreateReversal(" ", null, DateTime.UtcNow, null, null);

        act.Should().Throw<ArgumentException>();
    }
}
