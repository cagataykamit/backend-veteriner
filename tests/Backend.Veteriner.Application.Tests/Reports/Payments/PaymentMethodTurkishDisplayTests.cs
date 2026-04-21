using Backend.Veteriner.Application.Reports.Payments;
using Backend.Veteriner.Domain.Payments;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Reports.Payments;

public sealed class PaymentMethodTurkishDisplayTests
{
    [Theory]
    [InlineData(PaymentMethod.Cash, "Nakit")]
    [InlineData(PaymentMethod.Card, "Kart")]
    [InlineData(PaymentMethod.Transfer, "Havale-EFT")]
    public void ToLabel_Should_Map_Known_Values(PaymentMethod method, string expected)
        => PaymentMethodTurkishDisplay.ToLabel(method).Should().Be(expected);
}
