using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Payments;
using FluentAssertions;

namespace Backend.Veteriner.Domain.Tests.Payments;

public sealed class PaymentAllocationTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = DateTime.UtcNow;

    private static Charge NewCharge(decimal amount, Guid? clientId = null, Guid? clinicId = null)
        => Charge.Create(
                TenantId, clinicId ?? ClinicId, clientId ?? ClientId, null, null, null,
                "Muayene", 1m, amount, UserId, Now, null)
            .Value!;

    private static Payment NewPayment(decimal amount, string currency = "TRY")
        => new(TenantId, ClinicId, ClientId, null, null, null, amount, currency, PaymentMethod.Cash, Now, null);

    [Fact]
    public void AllocateFully_Should_Close_Charge_With_Two_Payments()
    {
        // ADR-012 kabul örneği: 1.000 TL ücret, 400 + 600 tahsilat.
        var charge = NewCharge(1000m);

        var first = NewPayment(400m);
        first.AllocateFully([(charge, 400m)], UserId, Now).IsSuccess.Should().BeTrue();
        charge.OutstandingAmount.Should().Be(600m);

        var second = NewPayment(600m);
        var result = second.AllocateFully([(charge, 600m)], UserId, Now);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().ContainSingle(a => a.Amount == 600m && a.ChargeId == charge.Id && a.PaymentId == second.Id);
        charge.AllocatedAmount.Should().Be(1000m);
        charge.OutstandingAmount.Should().Be(0m);
        second.AllocatedAmount.Should().Be(600m);
        second.UnallocatedAmount.Should().Be(0m);
    }

    [Fact]
    public void AllocateFully_Should_Reject_Overpayment_And_Change_Nothing()
    {
        var charge = NewCharge(1000m);
        NewPayment(400m).AllocateFully([(charge, 400m)], UserId, Now);

        var payment = NewPayment(601m);
        var result = payment.AllocateFully([(charge, 601m)], UserId, Now);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("PaymentAllocations.ExceedsOutstanding");
        charge.AllocatedAmount.Should().Be(400m);
        payment.AllocatedAmount.Should().Be(0m);
    }

    [Fact]
    public void AllocateFully_Should_Require_Sum_Equal_To_Payment_Amount()
    {
        var charge = NewCharge(1000m);

        NewPayment(500m).AllocateFully([(charge, 400m)], UserId, Now).Error.Code
            .Should().Be("Payments.AllocationSumMismatch");
        NewPayment(500m).AllocateFully([(charge, 600m)], UserId, Now).Error.Code
            .Should().Be("Payments.AllocationSumMismatch");
        charge.AllocatedAmount.Should().Be(0m);
    }

    [Fact]
    public void AllocateFully_Should_Split_One_Payment_Across_Two_Charges()
    {
        var a = NewCharge(300m);
        var b = NewCharge(200m);
        var payment = NewPayment(500m);

        var result = payment.AllocateFully([(a, 300m), (b, 200m)], UserId, Now);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(2);
        a.OutstandingAmount.Should().Be(0m);
        b.OutstandingAmount.Should().Be(0m);
        payment.AllocatedAmount.Should().Be(500m);
    }

    [Fact]
    public void AllocateFully_Should_Not_Mutate_Earlier_Charge_When_A_Later_Line_Fails()
    {
        var first = NewCharge(300m);
        var second = NewCharge(100m);
        var payment = NewPayment(450m);

        var result = payment.AllocateFully([(first, 300m), (second, 150m)], UserId, Now);

        result.Error.Code.Should().Be("PaymentAllocations.ExceedsOutstanding");
        first.AllocatedAmount.Should().Be(0m);
        second.AllocatedAmount.Should().Be(0m);
        payment.AllocatedAmount.Should().Be(0m);
    }

    [Fact]
    public void AllocateFully_Should_Reject_Empty_Duplicate_And_Invalid_Lines()
    {
        var charge = NewCharge(1000m);

        NewPayment(100m).AllocateFully([], UserId, Now).Error.Code.Should().Be("Payments.AllocationsRequired");
        NewPayment(200m).AllocateFully([(charge, 100m), (charge, 100m)], UserId, Now).Error.Code
            .Should().Be("Payments.Validation");
        NewPayment(100m).AllocateFully([(charge, 0m)], UserId, Now).Error.Code.Should().Be("Payments.Validation");
        NewPayment(100m).AllocateFully([(charge, 100.001m)], UserId, Now).Error.Code.Should().Be("Payments.Validation");
    }

    [Fact]
    public void AllocateFully_Should_Reject_Other_Client_Or_Clinic_Or_Currency()
    {
        NewPayment(100m).AllocateFully([(NewCharge(500m, clientId: Guid.NewGuid()), 100m)], UserId, Now).Error.Code
            .Should().Be("PaymentAllocations.ChargeClientMismatch");
        NewPayment(100m).AllocateFully([(NewCharge(500m, clinicId: Guid.NewGuid()), 100m)], UserId, Now).Error.Code
            .Should().Be("PaymentAllocations.ChargeClinicMismatch");
        NewPayment(100m, currency: "USD").AllocateFully([(NewCharge(500m), 100m)], UserId, Now).Error.Code
            .Should().Be("Payments.Validation");
    }

    [Fact]
    public void AllocateFully_Should_Reject_Second_Allocation_On_Same_Payment()
    {
        var charge = NewCharge(1000m);
        var payment = NewPayment(100m);
        payment.AllocateFully([(charge, 100m)], UserId, Now).IsSuccess.Should().BeTrue();

        payment.AllocateFully([(charge, 100m)], UserId, Now).IsSuccess.Should().BeFalse();
        charge.AllocatedAmount.Should().Be(100m);
    }

    [Fact]
    public void ApplyAllocation_Should_Advance_MutationSequence_On_Both_Aggregates()
    {
        var charge = NewCharge(100m);
        var payment = NewPayment(100m);

        payment.AllocateFully([(charge, 100m)], UserId, Now).IsSuccess.Should().BeTrue();

        charge.MutationSequence.Should().Be(1);
        payment.MutationSequence.Should().Be(1);
    }

    [Fact]
    public void UpdateDetails_Should_Block_Correction_Fields_When_Allocated_But_Allow_Others()
    {
        var charge = NewCharge(100m);
        var payment = NewPayment(100m);
        payment.AllocateFully([(charge, 100m)], UserId, Now);

        var amount = payment.UpdateDetails(ClinicId, ClientId, null, null, null, 150m, "TRY", PaymentMethod.Cash, Now, null);
        var client = payment.UpdateDetails(ClinicId, Guid.NewGuid(), null, null, null, 100m, "TRY", PaymentMethod.Cash, Now, null);
        var clinic = payment.UpdateDetails(Guid.NewGuid(), ClientId, null, null, null, 100m, "TRY", PaymentMethod.Cash, Now, null);
        var currency = payment.UpdateDetails(ClinicId, ClientId, null, null, null, 100m, "USD", PaymentMethod.Cash, Now, null);
        var ordinary = payment.UpdateDetails(ClinicId, ClientId, null, null, null, 100m, "TRY", PaymentMethod.Card, Now, "not");

        new[] { amount, client, clinic, currency }.Should()
            .OnlyContain(r => !r.IsSuccess && r.Error.Code == "Payments.UpdateBlockedByAllocation");
        ordinary.IsSuccess.Should().BeTrue();
        payment.Method.Should().Be(PaymentMethod.Card);
        payment.Amount.Should().Be(100m);
    }

    [Fact]
    public void UpdateDetails_Should_Allow_Correction_Fields_On_Unallocated_Legacy_Payment()
    {
        var payment = NewPayment(100m);

        payment.IsCorrection(ClinicId, ClientId, 150m, "TRY").Should().BeTrue();
        payment.IsCorrection(ClinicId, ClientId, 100m, "try").Should().BeFalse();
        payment.UpdateDetails(ClinicId, ClientId, null, null, null, 150m, "TRY", PaymentMethod.Cash, Now, null)
            .IsSuccess.Should().BeTrue();
        payment.Amount.Should().Be(150m);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("abc", false)]
    [InlineData("    ab    ", false)]
    [InlineData("yanlis tutar girildi", true)]
    public void ValidateCorrectionReason_Should_Require_5_To_500_Chars(string? reason, bool ok)
        => Payment.ValidateCorrectionReason(reason).IsSuccess.Should().Be(ok);

    [Fact]
    public void ValidateCorrectionReason_Should_Reject_Too_Long()
        => Payment.ValidateCorrectionReason(new string('a', 501)).IsSuccess.Should().BeFalse();

    [Fact]
    public void IsSupportedCurrency_Should_Accept_Only_Try()
    {
        Payment.IsSupportedCurrency("TRY").Should().BeTrue();
        Payment.IsSupportedCurrency(" try ").Should().BeTrue();
        Payment.IsSupportedCurrency("USD").Should().BeFalse();
        Payment.IsSupportedCurrency(null).Should().BeFalse();
    }
}

