using Backend.Veteriner.Domain.Tenants;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Tenants;

/// <summary>Sağlayıcının doğruladığı ödeme, bizde kapanmış oturumu da tamamlar; elle tamamlama yalnız açık oturumda.</summary>
public sealed class BillingCheckoutSessionVerifiedPaymentTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc);

    private static BillingCheckoutSession Session()
    {
        var session = BillingCheckoutSession.CreatePending(
            Guid.NewGuid(), SubscriptionPlanCode.Basic, SubscriptionPlanCode.Pro, BillingProvider.Iyzico, Now, Now.AddMinutes(30));
        session.SetRedirectReady("https://sandbox.example/checkout?token=t", "t", Now);
        return session;
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("cancelled")]
    [InlineData("failed")]
    public void MarkCompletedByVerifiedPayment_Should_Complete_ClosedSession(string closedAs)
    {
        var session = Session();
        switch (closedAs)
        {
            case "expired": session.MarkExpired(Now.AddMinutes(31)); break;
            case "cancelled": session.MarkCancelled(Now.AddMinutes(5)); break;
            case "failed": session.MarkFailed(Now.AddMinutes(5)); break;
        }

        session.MarkCompletedByVerifiedPayment(Now.AddMinutes(40), "pay-1");

        session.Status.Should().Be(BillingCheckoutSessionStatus.Completed);
        session.CompletedAtUtc.Should().Be(Now.AddMinutes(40));
        session.ExternalReference.Should().Be("pay-1");
    }

    [Fact]
    public void MarkCompletedByVerifiedPayment_Should_Reject_AlreadyCompleted()
    {
        var session = Session();
        session.MarkCompleted(Now.AddMinutes(1));

        var act = () => session.MarkCompletedByVerifiedPayment(Now.AddMinutes(2));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkCompleted_Should_Still_Reject_ClosedSession()
    {
        var session = Session();
        session.MarkCancelled(Now.AddMinutes(1));

        var act = () => session.MarkCompleted(Now.AddMinutes(2));

        act.Should().Throw<InvalidOperationException>();
    }
}
