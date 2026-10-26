using Backend.Veteriner.Application.Payments.Commands.Create;
using Backend.Veteriner.Domain.Charges;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Payments;

public sealed class PaymentChargeContextTests
{
    private static Charge Charge(Guid? petId, Guid? examinationId) =>
        PaymentAllocationTestSupport.NewCharge(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100m, petId, examinationId);

    [Fact]
    public void Single_charge_context_is_returned()
    {
        var pet = Guid.NewGuid();
        var exam = Guid.NewGuid();

        PaymentChargeContext.CommonPetId([Charge(pet, exam)]).Should().Be(pet);
        PaymentChargeContext.CommonExaminationId([Charge(pet, exam)]).Should().Be(exam);
    }

    [Fact]
    public void Charges_of_same_pet_give_that_pet()
    {
        var pet = Guid.NewGuid();

        PaymentChargeContext.CommonPetId([Charge(pet, Guid.NewGuid()), Charge(pet, null)]).Should().Be(pet);
    }

    [Fact]
    public void Charges_of_different_pets_give_null()
    {
        PaymentChargeContext.CommonPetId([Charge(Guid.NewGuid(), null), Charge(Guid.NewGuid(), null)]).Should().BeNull();
    }

    [Fact]
    public void Pet_charge_mixed_with_petless_charge_gives_null()
    {
        PaymentChargeContext.CommonPetId([Charge(Guid.NewGuid(), null), Charge(null, null)]).Should().BeNull();
    }
}