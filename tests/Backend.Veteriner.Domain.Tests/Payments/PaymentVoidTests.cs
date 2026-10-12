using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Payments;
using FluentAssertions;

namespace Backend.Veteriner.Domain.Tests.Payments;

public sealed class PaymentVoidTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);
    private const string Reason = "Yanlış müşteriye alındı";

    private static Charge NewCharge(decimal amount)
        => Charge.Create(TenantId, ClinicId, ClientId, null, null, null, "Muayene", 1m, amount, UserId, Now, null).Value!;

    private static Payment NewPayment(decimal amount)
        => new(TenantId, ClinicId, ClientId, null, null, null, amount, "TRY", PaymentMethod.Cash, Now, null);

    [Fact]
    public void Void_Should_Reverse_Allocations_And_Restore_Outstanding()
    {
        // 1.000 TL ücret: 400 + 600; 600'lük ödeme iptal edilince açık tutar 600 olur.
        var charge = NewCharge(1000m);
        NewPayment(400m).AllocateFully([(charge, 400m)], UserId, Now);
        var second = NewPayment(600m);
        var allocations = second.AllocateFully([(charge, 600m)], UserId, Now).Value!;
        charge.OutstandingAmount.Should().Be(0m);

        var result = second.Void([(allocations[0], charge)], UserId, Reason, Now);

        result.IsSuccess.Should().BeTrue();
        second.IsVoided.Should().BeTrue();
        second.VoidedByUserId.Should().Be(UserId);
        second.VoidReason.Should().Be(Reason);
        second.AllocatedAmount.Should().Be(0m);
        allocations[0].IsReversed.Should().BeTrue();
        charge.AllocatedAmount.Should().Be(400m);
        charge.OutstandingAmount.Should().Be(600m);
    }

    [Fact]
    public void Void_Should_Reverse_All_Charges_Of_A_Split_Payment()
    {
        var a = NewCharge(300m);
        var b = NewCharge(500m);
        var payment = NewPayment(500m);
        var allocations = payment.AllocateFully([(a, 300m), (b, 200m)], UserId, Now).Value!;

        var result = payment.Void([(allocations[0], a), (allocations[1], b)], UserId, Reason, Now);

        result.IsSuccess.Should().BeTrue();
        a.AllocatedAmount.Should().Be(0m);
        b.AllocatedAmount.Should().Be(0m);
    }

    [Fact]
    public void Void_Legacy_Unallocated_Payment_Should_Succeed_Without_Allocations()
    {
        var payment = NewPayment(250m);

        payment.Void([], UserId, Reason, Now).IsSuccess.Should().BeTrue();

        payment.IsVoided.Should().BeTrue();
    }

    [Fact]
    public void Void_Should_Require_Reason_And_Change_Nothing_On_Failure()
    {
        var charge = NewCharge(1000m);
        var payment = NewPayment(1000m);
        var allocations = payment.AllocateFully([(charge, 1000m)], UserId, Now).Value!;

        payment.Void([(allocations[0], charge)], UserId, "kısa", Now).Error.Code.Should().Be("Payments.Validation");

        payment.IsVoided.Should().BeFalse();
        charge.AllocatedAmount.Should().Be(1000m);
        allocations[0].IsReversed.Should().BeFalse();
    }

    [Fact]
    public void Void_Should_Reject_Allocations_Inconsistent_With_Counter()
    {
        var charge = NewCharge(1000m);
        var payment = NewPayment(1000m);
        payment.AllocateFully([(charge, 1000m)], UserId, Now);

        // Aktif tahsis listesi eksik verilirse sayaçla tutarsızdır.
        payment.Void([], UserId, Reason, Now).Error.Code.Should().Be("Payments.Validation");
        charge.AllocatedAmount.Should().Be(1000m);
    }

    [Fact]
    public void Voided_Payment_Cannot_Be_Voided_Updated_Or_Allocated_Again()
    {
        var payment = NewPayment(100m);
        payment.Void([], UserId, Reason, Now);

        payment.Void([], UserId, Reason, Now).Error.Code.Should().Be("Payments.AlreadyVoided");
        payment.UpdateDetails(ClinicId, ClientId, null, null, null, 100m, "TRY", PaymentMethod.Card, Now, null)
            .Error.Code.Should().Be("Payments.AlreadyVoided");
        payment.ApplyAllocation(10m).Error.Code.Should().Be("Payments.AlreadyVoided");
    }
}
