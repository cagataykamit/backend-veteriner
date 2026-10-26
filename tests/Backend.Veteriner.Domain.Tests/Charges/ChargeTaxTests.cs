using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Products;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Charges;

/// <summary>ADR-036 Dilim 1: ücret satır türü ve KDV oranı.</summary>
public sealed class ChargeTaxTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);
    private const string Reason = "KDV oranı yanlış seçildi";

    private static Charge NewCharge(ChargeTax? tax = null)
        => Charge.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, null, "Muayene", 1m, 500m,
            Guid.NewGuid(), Now, null, tax).Value!;

    [Fact]
    public void Create_Without_Tax_Should_Default_To_Service_And_Clinic_Vat()
    {
        var charge = NewCharge();

        charge.LineKind.Should().Be(ChargeLineKind.Service);
        charge.VatRate.Should().BeNull();
    }

    [Fact]
    public void Create_With_Tax_Should_Store_Line_Kind_And_Rate()
    {
        var charge = NewCharge(new ChargeTax(ChargeLineKind.Product, 10m));

        charge.LineKind.Should().Be(ChargeLineKind.Product);
        charge.VatRate.Should().Be(10m);
    }

    [Theory]
    [InlineData(101)]
    [InlineData(-1)]
    [InlineData(10.123)]
    public void Create_With_Invalid_Vat_Should_Fail(double rate)
    {
        var result = Charge.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, null, "Muayene", 1m, 500m,
            Guid.NewGuid(), Now, null, new ChargeTax(ChargeLineKind.Service, (decimal)rate));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Charges.Validation");
    }

    [Fact]
    public void Create_With_Undefined_Line_Kind_Should_Fail()
        => Charge.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, null, "Muayene", 1m, 500m,
                Guid.NewGuid(), Now, null, new ChargeTax((ChargeLineKind)7, null))
            .IsSuccess.Should().BeFalse();

    [Fact]
    public void Correct_Only_Tax_Should_Count_As_Change()
    {
        var charge = NewCharge();

        var result = charge.Correct("Muayene", 1m, 500m, Reason, new ChargeTax(ChargeLineKind.Product, 20m));

        result.IsSuccess.Should().BeTrue();
        charge.LineKind.Should().Be(ChargeLineKind.Product);
        charge.VatRate.Should().Be(20m);
        charge.MutationSequence.Should().Be(1);
    }

    [Fact]
    public void Correct_Without_Tax_Should_Keep_Tax_Fields()
    {
        var charge = NewCharge(new ChargeTax(ChargeLineKind.Product, 10m));

        charge.Correct("Mama", 2m, 500m, Reason).IsSuccess.Should().BeTrue();

        charge.LineKind.Should().Be(ChargeLineKind.Product);
        charge.VatRate.Should().Be(10m);
    }

    [Fact]
    public void Correct_With_Same_Tax_And_Details_Should_Be_Rejected()
    {
        var charge = NewCharge(new ChargeTax(ChargeLineKind.Product, 10m));

        var result = charge.Correct("Muayene", 1m, 500m, Reason, new ChargeTax(ChargeLineKind.Product, 10m));

        result.IsSuccess.Should().BeFalse();
        result.Error.Message.Should().Contain("en az bir alan");
    }

    [Theory]
    [InlineData(ProductKind.Service, ChargeLineKind.Service)]
    [InlineData(ProductKind.Medicine, ChargeLineKind.Product)]
    [InlineData(ProductKind.Vaccine, ChargeLineKind.Product)]
    [InlineData(ProductKind.Other, ChargeLineKind.Product)]
    public void Line_Kind_Should_Derive_From_Product_Kind(ProductKind kind, ChargeLineKind expected)
        => ChargeLineKinds.FromProductKind(kind).Should().Be(expected);
}
