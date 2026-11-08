using Backend.Veteriner.Domain.Payments;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Payments;

/// <summary>TIER-008: kasa yalnız nakitte; yöntem ya da klinik değişince bağ kalkar (nakit Ana kasaya düşer).</summary>
public sealed class PaymentCashRegisterTests
{
    private static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private static Payment New(PaymentMethod method, Guid clinicId)
        => new(Guid.NewGuid(), clinicId, Guid.NewGuid(), null, null, null, 100m, "TRY", method, Now, null);

    [Fact]
    public void Cash_Register_Should_Only_Be_Assigned_To_Cash()
    {
        var registerId = Guid.NewGuid();

        var cash = New(PaymentMethod.Cash, Guid.NewGuid());
        cash.AssignCashRegister(registerId).IsSuccess.Should().BeTrue();
        cash.CashRegisterId.Should().Be(registerId);

        var card = New(PaymentMethod.Card, Guid.NewGuid());
        card.AssignCashRegister(registerId).Error.Code.Should().Be("Payments.Validation");
        card.CashRegisterId.Should().BeNull();
    }

    [Fact]
    public void Update_Should_Drop_The_Register_When_Method_Or_Clinic_Changes()
    {
        var clinicId = Guid.NewGuid();
        var payment = New(PaymentMethod.Cash, clinicId);
        payment.AssignCashRegister(Guid.NewGuid());

        payment.UpdateDetails(clinicId, payment.ClientId, null, null, null, 100m, "TRY", PaymentMethod.Cash, Now, "not").IsSuccess.Should().BeTrue();
        payment.CashRegisterId.Should().NotBeNull("yalnız not değişti");

        payment.UpdateDetails(clinicId, payment.ClientId, null, null, null, 100m, "TRY", PaymentMethod.Card, Now, null).IsSuccess.Should().BeTrue();
        payment.CashRegisterId.Should().BeNull();

        var moved = New(PaymentMethod.Cash, clinicId);
        moved.AssignCashRegister(Guid.NewGuid());
        moved.UpdateDetails(Guid.NewGuid(), moved.ClientId, null, null, null, 100m, "TRY", PaymentMethod.Cash, Now, null).IsSuccess.Should().BeTrue();
        moved.CashRegisterId.Should().BeNull("başka kliniğin kasası olamaz");
    }
}
