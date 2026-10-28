using AwesomeAssertions;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Domain.Tests.Tenants;

public sealed class BillingCheckoutSessionDuplicatePaymentTests
{
    private static BillingCheckoutSession OpenSession()
        => BillingCheckoutSession.CreatePending(
            Guid.NewGuid(), SubscriptionPlanCode.Basic, SubscriptionPlanCode.Pro, BillingProvider.Iyzico, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(30));

    [Fact]
    public void MarkDuplicatePayment_Should_CloseOpenSession_And_QueueRefund()
    {
        var session = OpenSession();

        session.MarkDuplicatePayment(DateTime.UtcNow);

        session.Status.Should().Be(BillingCheckoutSessionStatus.Cancelled);
        session.RefundStatus.Should().Be(BillingCheckoutRefundStatus.Pending);
        session.DuplicatePaymentDetectedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void MarkDuplicatePayment_Should_KeepClosedStatus()
    {
        var session = OpenSession();
        session.MarkExpired(DateTime.UtcNow);

        session.MarkDuplicatePayment(DateTime.UtcNow);

        session.Status.Should().Be(BillingCheckoutSessionStatus.Expired);
        session.RefundStatus.Should().Be(BillingCheckoutRefundStatus.Pending);
    }

    [Fact]
    public void MarkDuplicatePayment_Should_Reject_CompletedSession()
    {
        var session = OpenSession();
        session.MarkCompleted(DateTime.UtcNow);

        var act = () => session.MarkDuplicatePayment(DateTime.UtcNow);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RecordRefundFailed_Should_KeepPending_Until_ManualRequired()
    {
        var session = OpenSession();
        session.MarkDuplicatePayment(DateTime.UtcNow);

        session.RecordRefundFailed(DateTime.UtcNow, "geçici", requiresManualRefund: false);
        session.RefundStatus.Should().Be(BillingCheckoutRefundStatus.Pending);
        session.RefundAttemptCount.Should().Be(1);

        session.RecordRefundFailed(DateTime.UtcNow, new string('x', 600), requiresManualRefund: true);
        session.RefundStatus.Should().Be(BillingCheckoutRefundStatus.ManualRequired);
        session.RefundAttemptCount.Should().Be(2);
        session.RefundFailureReason!.Length.Should().Be(BillingCheckoutSession.RefundFailureReasonMaxLength);
    }

    [Fact]
    public void RecordRefundSucceeded_Should_MarkRefunded_And_ClearReason()
    {
        var session = OpenSession();
        session.MarkDuplicatePayment(DateTime.UtcNow);
        session.RecordRefundFailed(DateTime.UtcNow, "geçici", requiresManualRefund: false);

        session.RecordRefundSucceeded(DateTime.UtcNow);

        session.RefundStatus.Should().Be(BillingCheckoutRefundStatus.Refunded);
        session.RefundedAtUtc.Should().NotBeNull();
        session.RefundFailureReason.Should().BeNull();
    }

    [Fact]
    public void MarkRefundedManually_Should_Require_ManualRequired_And_BeIdempotent()
    {
        var session = OpenSession();
        session.MarkDuplicatePayment(DateTime.UtcNow);

        var whilePending = () => session.MarkRefundedManually(DateTime.UtcNow);
        whilePending.Should().Throw<InvalidOperationException>();

        session.RecordRefundFailed(DateTime.UtcNow, "reddedildi", requiresManualRefund: true);
        session.MarkRefundedManually(DateTime.UtcNow);
        session.RefundStatus.Should().Be(BillingCheckoutRefundStatus.Refunded);

        var again = () => session.MarkRefundedManually(DateTime.UtcNow);
        again.Should().NotThrow();
    }
}
