using Backend.Veteriner.Application.Payments.Queries;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Payments;

public sealed class PaymentPetCountTests
{
    [Theory]
    [InlineData(false, 0, 0)]
    [InlineData(true, 0, 1)]
    [InlineData(false, 1, 1)]
    [InlineData(true, 1, 1)]
    [InlineData(false, 3, 3)]
    public void Resolve_uses_allocated_pets_and_falls_back_to_payment_pet(bool hasPaymentPet, int allocated, int expected)
        => PaymentPetCount.Resolve(hasPaymentPet ? Guid.NewGuid() : null, allocated).Should().Be(expected);
}