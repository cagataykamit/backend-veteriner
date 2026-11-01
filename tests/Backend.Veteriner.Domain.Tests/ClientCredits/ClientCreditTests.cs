using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.ClientCredits;
using Backend.Veteriner.Domain.Payments;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.ClientCredits;

/// <summary>FIN-005: fazla ödeme/avans kredisi, mahsup, geri alma, iade ve ödeme iptaliyle etkileşim.</summary>
public sealed class ClientCreditTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);

    private static Charge NewCharge(decimal amount, Guid? clientId = null, Guid? clinicId = null)
        => Charge.Create(
                TenantId, clinicId ?? ClinicId, clientId ?? ClientId, null, null, null,
                "Muayene", 1m, amount, UserId, Now, null)
            .Value!;

    private static Payment NewPayment(decimal amount)
        => new(TenantId, ClinicId, ClientId, null, null, null, amount, "TRY", PaymentMethod.Cash, Now, null);

    private static ClientCredit NewCredit(decimal amount, DateTime? at = null)
        => NewPayment(amount).Settle([], amount, UserId, at ?? Now).Value!.Credit!;

    [Fact]
    public void Settle_Should_Turn_Overpayment_Into_Credit()
    {
        // 1.000 TL ödeme, 600 TL açık ücret → 400 TL kredi.
        var charge = NewCharge(600m);
        var payment = NewPayment(1000m);

        var result = payment.Settle([(charge, 600m)], 400m, UserId, Now);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Allocations.Should().ContainSingle(a => a.Amount == 600m);
        var credit = result.Value.Credit!;
        credit.Source.Should().Be(ClientCreditSource.Overpayment);
        credit.Amount.Should().Be(400m);
        credit.RemainingAmount.Should().Be(400m);
        credit.SourcePaymentId.Should().Be(payment.Id);
        payment.AllocatedAmount.Should().Be(600m);
        payment.CreditedAmount.Should().Be(400m);
        payment.UnallocatedAmount.Should().Be(0m);
        charge.OutstandingAmount.Should().Be(0m);
    }

    [Fact]
    public void Settle_Without_Allocations_Should_Be_Prepayment()
    {
        var payment = NewPayment(500m);

        var result = payment.Settle([], 500m, UserId, Now);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Allocations.Should().BeEmpty();
        result.Value.Credit!.Source.Should().Be(ClientCreditSource.Prepayment);
        payment.AllocatedAmount.Should().Be(0m);
        payment.CreditedAmount.Should().Be(500m);
    }

    [Theory]
    [InlineData(300, "Payments.AllocationSumMismatch")]
    [InlineData(-1, "Payments.Validation")]
    [InlineData(0.001, "Payments.Validation")]
    public void Settle_Should_Reject_Invalid_Credit_And_Change_Nothing(decimal creditAmount, string code)
    {
        var charge = NewCharge(600m);
        var payment = NewPayment(1000m);

        var result = payment.Settle([(charge, 600m)], creditAmount, UserId, Now);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(code);
        payment.CreditedAmount.Should().Be(0m);
        payment.AllocatedAmount.Should().Be(0m);
        charge.AllocatedAmount.Should().Be(0m);
    }

    [Fact]
    public void Settle_Prepayment_Must_Equal_Amount()
    {
        NewPayment(500m).Settle([], 400m, UserId, Now).Error.Code.Should().Be("Payments.AllocationSumMismatch");
        NewPayment(500m).Settle([], 0m, UserId, Now).Error.Code.Should().Be("Payments.AllocationsRequired");
    }

    [Fact]
    public void Ledger_Should_Consume_Oldest_Credit_First_Across_Credits()
    {
        var older = NewCredit(100m, Now.AddDays(-2));
        var newer = NewCredit(300m, Now.AddDays(-1));
        var charge = NewCharge(250m);

        var result = ClientCreditLedger.Apply([newer, older], [(charge, 250m)], UserId, Guid.NewGuid(), Now);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(2);
        result.Value[0].CreditId.Should().Be(older.Id);
        result.Value[0].Amount.Should().Be(100m);
        result.Value[1].CreditId.Should().Be(newer.Id);
        result.Value[1].Amount.Should().Be(150m);
        older.RemainingAmount.Should().Be(0m);
        older.Status.Should().Be(ClientCreditStatus.Used);
        newer.RemainingAmount.Should().Be(150m);
        charge.AllocatedAmount.Should().Be(250m);
        charge.OutstandingAmount.Should().Be(0m);
    }

    [Fact]
    public void Ledger_Should_Reject_When_Balance_Insufficient_And_Change_Nothing()
    {
        var credit = NewCredit(100m);
        var charge = NewCharge(250m);

        var result = ClientCreditLedger.Apply([credit], [(charge, 150m)], UserId, null, Now);

        result.Error.Code.Should().Be("ClientCredits.InsufficientBalance");
        credit.UsedAmount.Should().Be(0m);
        charge.AllocatedAmount.Should().Be(0m);
    }

    [Fact]
    public void Ledger_Should_Reject_More_Than_Outstanding()
    {
        var credit = NewCredit(500m);
        var charge = NewCharge(200m);

        ClientCreditLedger.Apply([credit], [(charge, 201m)], UserId, null, Now)
            .Error.Code.Should().Be("PaymentAllocations.ExceedsOutstanding");
    }

    [Fact]
    public void Apply_Should_Reject_Other_Client_Or_Clinic()
    {
        var credit = NewCredit(500m);

        credit.Apply(NewCharge(100m, clientId: Guid.NewGuid()), 50m, UserId, null, Now)
            .Error.Code.Should().Be("ClientCredits.ChargeClientMismatch");
        credit.Apply(NewCharge(100m, clinicId: Guid.NewGuid()), 50m, UserId, null, Now)
            .Error.Code.Should().Be("ClientCredits.ChargeClinicMismatch");
        credit.UsedAmount.Should().Be(0m);
    }

    [Fact]
    public void ReverseApplication_Should_Restore_Credit_And_Charge()
    {
        var credit = NewCredit(300m);
        var charge = NewCharge(200m);
        var application = credit.Apply(charge, 200m, UserId, null, Now).Value!;

        var result = credit.ReverseApplication(application, charge, UserId, "Yanlış ücret seçildi", Now);

        result.IsSuccess.Should().BeTrue();
        credit.RemainingAmount.Should().Be(300m);
        charge.OutstandingAmount.Should().Be(200m);
        application.IsReversed.Should().BeTrue();
        application.ReverseReason.Should().Be("Yanlış ücret seçildi");
        credit.ReverseApplication(application, charge, UserId, "Yanlış ücret seçildi", Now)
            .Error.Code.Should().Be("ClientCredits.ApplicationAlreadyReversed");
    }

    [Fact]
    public void ReverseApplication_Requires_Reason()
    {
        var credit = NewCredit(300m);
        var charge = NewCharge(200m);
        var application = credit.Apply(charge, 200m, UserId, null, Now).Value!;

        credit.ReverseApplication(application, charge, UserId, "kısa", Now).Error.Code.Should().Be("ClientCredits.Validation");
        charge.AllocatedAmount.Should().Be(200m);
    }

    [Fact]
    public void Refund_Should_Return_Remaining_And_Close_Credit()
    {
        var credit = NewCredit(300m);
        credit.Apply(NewCharge(100m), 100m, UserId, null, Now);

        var result = credit.Refund(UserId, "Müşteri parasını geri istedi", PaymentMethod.Cash, Now);

        result.IsSuccess.Should().BeTrue();
        credit.RefundedAmount.Should().Be(200m);
        credit.RemainingAmount.Should().Be(0m);
        credit.Status.Should().Be(ClientCreditStatus.Refunded);
        credit.Refund(UserId, "Müşteri parasını geri istedi", PaymentMethod.Cash, Now)
            .Error.Code.Should().Be("ClientCredits.NothingToRefund");
        credit.Apply(NewCharge(50m), 10m, UserId, null, Now).Error.Code.Should().Be("ClientCredits.CreditClosed");
    }

    [Fact]
    public void Refund_Of_Fully_Used_Credit_Is_Rejected()
    {
        var credit = NewCredit(100m);
        credit.Apply(NewCharge(100m), 100m, UserId, null, Now);

        credit.Refund(UserId, "Müşteri parasını geri istedi", PaymentMethod.Cash, Now)
            .Error.Code.Should().Be("ClientCredits.NothingToRefund");
    }

    [Fact]
    public void Void_Payment_Should_Void_Unused_Credit()
    {
        var charge = NewCharge(600m);
        var payment = NewPayment(1000m);
        var settled = payment.Settle([(charge, 600m)], 400m, UserId, Now).Value!;

        var result = payment.Void([(settled.Allocations[0], charge)], UserId, "Yanlış müşteri", Now, settled.Credit);

        result.IsSuccess.Should().BeTrue();
        settled.Credit!.Status.Should().Be(ClientCreditStatus.Voided);
        settled.Credit.RemainingAmount.Should().Be(0m);
        charge.OutstandingAmount.Should().Be(600m);
    }

    [Fact]
    public void Void_Payment_Should_Be_Blocked_When_Credit_Used_Or_Refunded()
    {
        var payment = NewPayment(500m);
        var credit = payment.Settle([], 500m, UserId, Now).Value!.Credit!;
        var charge = NewCharge(100m);
        var application = credit.Apply(charge, 100m, UserId, null, Now).Value!;

        payment.Void([], UserId, "Yanlış müşteri", Now, credit).Error.Code.Should().Be("Payments.VoidBlockedByCredit");
        payment.IsVoided.Should().BeFalse();

        credit.ReverseApplication(application, charge, UserId, "Yanlış ücret seçildi", Now);
        credit.Refund(UserId, "Müşteri parasını geri istedi", PaymentMethod.Cash, Now);
        payment.Void([], UserId, "Yanlış müşteri", Now, credit).Error.Code.Should().Be("Payments.VoidBlockedByCredit");
    }

    [Fact]
    public void Void_Payment_With_Credit_Requires_Credit_Record()
    {
        var payment = NewPayment(500m);
        payment.Settle([], 500m, UserId, Now);

        payment.Void([], UserId, "Yanlış müşteri", Now).Error.Code.Should().Be("Payments.Validation");
    }

    [Fact]
    public void Charge_Void_Is_Blocked_By_Credit_Application()
    {
        var credit = NewCredit(100m);
        var charge = NewCharge(100m);
        credit.Apply(charge, 100m, UserId, null, Now);

        charge.Void(UserId, "Yanlış ücret", Now).Error.Code.Should().Be("Charges.VoidBlockedByAllocation");
    }
}
