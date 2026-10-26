using Backend.Veteriner.Domain.Charges;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Charges;

public sealed class ChargeTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

    private static Backend.Veteriner.Domain.Shared.Result<Charge> Create(
        Guid? clientId = null,
        string? description = "Genel muayene",
        decimal quantity = 1m,
        decimal unitPrice = 750m,
        Guid? visitId = null,
        Guid? examinationId = null,
        Guid? requestId = null)
        => Charge.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            clientId ?? Guid.NewGuid(),
            null,
            visitId,
            examinationId,
            description,
            quantity,
            unitPrice,
            Guid.NewGuid(),
            Now,
            requestId);

    [Fact]
    public void Create_Should_Succeed_Without_Visit_And_Examination_And_Default_To_Try()
    {
        var result = Create();

        result.IsSuccess.Should().BeTrue();
        var charge = result.Value!;
        charge.VisitId.Should().BeNull();
        charge.ExaminationId.Should().BeNull();
        charge.Currency.Should().Be("TRY");
        charge.Amount.Should().Be(750m);
        charge.CreatedAtUtc.Should().Be(Now);
        charge.Description.Should().Be("Genel muayene");
    }

    [Theory]
    [InlineData(2, 100.00, 200.00)]
    [InlineData(0.5, 99.99, 50.00)]      // 49.995 -> 50.00 (AwayFromZero)
    [InlineData(1.5, 0.01, 0.02)]        // 0.015 -> 0.02
    [InlineData(0.333, 10.00, 3.33)]
    public void Create_Should_Compute_Amount_From_Quantity_And_UnitPrice(double quantity, double unitPrice, double expected)
    {
        var result = Create(quantity: (decimal)quantity, unitPrice: (decimal)unitPrice);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Amount.Should().Be((decimal)expected);
    }

    [Fact]
    public void Create_Should_Fail_When_ClientId_Is_Empty()
    {
        var result = Create(clientId: Guid.Empty);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Charges.Validation");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Should_Fail_When_Description_Is_Blank(string? description)
    {
        Create(description: description).Error.Code.Should().Be("Charges.Validation");
    }

    [Fact]
    public void Create_Should_Fail_When_Description_Too_Long()
    {
        Create(description: new string('a', Charge.MaxDescriptionLength + 1)).IsSuccess.Should().BeFalse();
        Create(description: new string('a', Charge.MaxDescriptionLength)).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(100000.001, 10)]     // üst sınır aşımı
    [InlineData(1, 0)]
    [InlineData(1, -5)]
    [InlineData(1, 10000000.01)]     // üst sınır aşımı
    public void Create_Should_Fail_When_Quantity_Or_UnitPrice_Out_Of_Range(double quantity, double unitPrice)
    {
        var result = Create(quantity: (decimal)quantity, unitPrice: (decimal)unitPrice);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Charges.Validation");
    }

    [Fact]
    public void Create_Should_Fail_When_Too_Many_Decimals()
    {
        Create(quantity: 1.0001m).IsSuccess.Should().BeFalse();
        Create(unitPrice: 10.001m).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Create_Should_Fail_When_Amount_Rounds_To_Zero()
    {
        var result = Create(quantity: 0.001m, unitPrice: 0.01m);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Charges.Validation");
    }

    [Fact]
    public void Create_Should_Fail_When_Optional_Ids_Are_Empty_Guid()
    {
        Create(visitId: Guid.Empty).IsSuccess.Should().BeFalse();
        Create(examinationId: Guid.Empty).IsSuccess.Should().BeFalse();
        Create(requestId: Guid.Empty).IsSuccess.Should().BeFalse();
    }
}
