using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Billing;
using Backend.Veteriner.Application.Tenants.Billing;
using Backend.Veteriner.Domain.Tenants;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Tenants.Handlers;

public sealed class DuplicatePaymentRefunderTests
{
    private readonly Mock<IBillingCheckoutProviderResolver> _resolver = new();
    private readonly Mock<IBillingCheckoutProvider> _provider = new();
    private readonly Mock<IRepository<BillingCheckoutSession>> _sessionsWrite = new();

    private DuplicatePaymentRefunder Create()
    {
        _resolver.Setup(x => x.Resolve(BillingProvider.Iyzico)).Returns(_provider.Object);
        return new DuplicatePaymentRefunder(_resolver.Object, _sessionsWrite.Object, TimeProvider.System, NullLogger<DuplicatePaymentRefunder>.Instance);
    }

    private static BillingCheckoutSession DuplicateSession()
    {
        var session = BillingCheckoutSession.CreatePending(
            Guid.NewGuid(), SubscriptionPlanCode.Basic, SubscriptionPlanCode.Pro, BillingProvider.Iyzico, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(30));
        session.MarkDuplicatePayment(DateTime.UtcNow);
        return session;
    }

    [Fact]
    public async Task RefundAsync_Should_MarkRefunded_When_ProviderSucceeds()
    {
        var session = DuplicateSession();
        _provider.Setup(x => x.RefundPaymentAsync(session, It.IsAny<CancellationToken>())).ReturnsAsync(ProviderRefundResult.Success());

        var refunded = await Create().RefundAsync(session);

        refunded.Should().BeTrue();
        session.RefundStatus.Should().Be(BillingCheckoutRefundStatus.Refunded);
        _sessionsWrite.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefundAsync_Should_Retry_Then_RequireManual_After_MaxAttempts()
    {
        var session = DuplicateSession();
        _provider.Setup(x => x.RefundPaymentAsync(session, It.IsAny<CancellationToken>())).ReturnsAsync(ProviderRefundResult.Failed("ağ hatası"));
        var refunder = Create();

        for (var i = 1; i < DuplicatePaymentRefunder.MaxAutomaticAttempts; i++)
        {
            (await refunder.RefundAsync(session)).Should().BeFalse();
            session.RefundStatus.Should().Be(BillingCheckoutRefundStatus.Pending);
        }

        (await refunder.RefundAsync(session)).Should().BeFalse();
        session.RefundStatus.Should().Be(BillingCheckoutRefundStatus.ManualRequired);
        session.RefundAttemptCount.Should().Be(DuplicatePaymentRefunder.MaxAutomaticAttempts);
        session.RefundFailureReason.Should().Be("ağ hatası");
    }

    [Fact]
    public async Task RefundAsync_Should_RequireManual_Immediately_When_NotSupported()
    {
        var session = DuplicateSession();
        _provider.Setup(x => x.RefundPaymentAsync(session, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProviderRefundResult.NotSupported("otomatik iade yok"));

        await Create().RefundAsync(session);

        session.RefundStatus.Should().Be(BillingCheckoutRefundStatus.ManualRequired);
        session.RefundAttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task RefundAsync_Should_Skip_When_NotPending()
    {
        var session = DuplicateSession();
        session.RecordRefundSucceeded(DateTime.UtcNow);

        var refunded = await Create().RefundAsync(session);

        refunded.Should().BeFalse();
        _provider.VerifyNoOtherCalls();
        _sessionsWrite.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
