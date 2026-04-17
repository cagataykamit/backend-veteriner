using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Auditing;
using Backend.Veteriner.Application.Common.Billing;
using Backend.Veteriner.Application.Tenants.Commands.Billing;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Tenants.Handlers;

public sealed class ProcessBillingWebhookCommandHandlerTests
{
    private readonly Mock<IBillingWebhookSignatureVerifier> _signatureVerifier = new();
    private readonly Mock<IBillingWebhookPayloadParser> _payloadParser = new();
    private readonly Mock<ISubscriptionCheckoutActivationService> _activation = new();
    private readonly Mock<IReadRepository<BillingWebhookReceipt>> _receiptRead = new();
    private readonly Mock<IRepository<BillingWebhookReceipt>> _receiptWrite = new();
    private readonly Mock<IReadRepository<BillingCheckoutSession>> _sessionsRead = new();
    private readonly Mock<IRepository<BillingCheckoutSession>> _sessionsWrite = new();
    private readonly Mock<IAuditLogWriter> _auditWriter = new();
    private readonly Mock<IClientContext> _clientContext = new();

    private ProcessBillingWebhookCommandHandler CreateHandler()
        => new(
            _signatureVerifier.Object,
            _payloadParser.Object,
            _activation.Object,
            _receiptRead.Object,
            _receiptWrite.Object,
            _sessionsRead.Object,
            _sessionsWrite.Object,
            _auditWriter.Object,
            _clientContext.Object,
            NullLogger<ProcessBillingWebhookCommandHandler>.Instance);

    private static ProcessBillingWebhookCommand CreateCommand(BillingProvider provider = BillingProvider.Stripe)
        => new(provider, "{\"any\":\"payload\"}", new Dictionary<string, string>());

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_SignatureInvalid()
    {
        _signatureVerifier
            .Setup(x => x.Verify(It.IsAny<BillingProvider>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Returns(Result.Failure("Billing.WebhookSignatureInvalid", "invalid"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Billing.WebhookSignatureInvalid");
        _payloadParser.Verify(x => x.Parse(It.IsAny<BillingProvider>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_PayloadInvalid()
    {
        _signatureVerifier
            .Setup(x => x.Verify(It.IsAny<BillingProvider>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Returns(Result.Success());
        _payloadParser
            .Setup(x => x.Parse(It.IsAny<BillingProvider>(), It.IsAny<string>()))
            .Returns(Result<BillingWebhookNormalizedEvent>.Failure("Billing.WebhookPayloadInvalid", "bad payload"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Billing.WebhookPayloadInvalid");
        _activation.Verify(x => x.TryActivateAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<BillingProvider?>(),
                It.IsAny<string?>(),
                It.IsAny<BillingActivationSource>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnDuplicateAck_When_EventAlreadyProcessed()
    {
        var eventId = "evt_123";
        var parsed = new BillingWebhookNormalizedEvent(eventId, "checkout.session.completed", BillingWebhookEventKind.Ignored, null, null);
        var receipt = BillingWebhookReceipt.CreateReceived(BillingProvider.Stripe, eventId, parsed.EventType, null, "corr", DateTime.UtcNow);
        receipt.MarkProcessed(DateTime.UtcNow);

        _signatureVerifier.Setup(x => x.Verify(It.IsAny<BillingProvider>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Returns(Result.Success());
        _payloadParser.Setup(x => x.Parse(It.IsAny<BillingProvider>(), It.IsAny<string>()))
            .Returns(Result<BillingWebhookNormalizedEvent>.Success(parsed));
        _receiptRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<BillingWebhookReceiptByProviderEventIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(receipt);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Duplicate.Should().BeTrue();
        result.Value.Processed.Should().BeFalse();
        result.Value.ProviderEventId.Should().Be(eventId);
        _receiptWrite.Verify(x => x.AddAsync(It.IsAny<BillingWebhookReceipt>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditWriter.Verify(x => x.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_PaymentSucceeded_ActivationFailsWithProviderMismatch()
    {
        var sessionId = Guid.NewGuid();
        var parsed = new BillingWebhookNormalizedEvent("evt_success", "checkout.session.completed", BillingWebhookEventKind.PaymentSucceeded, sessionId, "pay_ref");

        _signatureVerifier.Setup(x => x.Verify(It.IsAny<BillingProvider>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Returns(Result.Success());
        _payloadParser.Setup(x => x.Parse(It.IsAny<BillingProvider>(), It.IsAny<string>()))
            .Returns(Result<BillingWebhookNormalizedEvent>.Success(parsed));
        _receiptRead.SetupSequence(x => x.FirstOrDefaultAsync(It.IsAny<BillingWebhookReceiptByProviderEventIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BillingWebhookReceipt?)null);
        _receiptWrite.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _activation.Setup(x => x.TryActivateAsync(sessionId, null, BillingProvider.Stripe, "pay_ref", BillingActivationSource.Webhook, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SubscriptionCheckoutSessionDto>.Failure("Billing.ProviderMismatch", "mismatch"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(BillingProvider.Stripe), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Billing.ProviderMismatch");
        _receiptWrite.Verify(x => x.UpdateAsync(It.IsAny<BillingWebhookReceipt>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditWriter.Verify(x => x.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnSuccessAndAudit_When_PaymentSucceeded_ActivationSucceeds()
    {
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var parsed = new BillingWebhookNormalizedEvent("evt_ok", "checkout.session.completed", BillingWebhookEventKind.PaymentSucceeded, sessionId, "ref_ok");
        var activationDto = new SubscriptionCheckoutSessionDto(
            sessionId,
            Guid.NewGuid(),
            "Basic",
            "Pro",
            BillingCheckoutSessionStatus.Completed,
            BillingProvider.Stripe,
            null,
            false,
            null,
            null,
            null,
            null);

        _clientContext.SetupGet(x => x.CorrelationId).Returns("corr-123");
        _clientContext.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        _clientContext.SetupGet(x => x.Path).Returns("/api/v1/webhooks/billing/stripe");
        _clientContext.SetupGet(x => x.Method).Returns("POST");

        _signatureVerifier.Setup(x => x.Verify(It.IsAny<BillingProvider>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Returns(Result.Success());
        _payloadParser.Setup(x => x.Parse(It.IsAny<BillingProvider>(), It.IsAny<string>()))
            .Returns(Result<BillingWebhookNormalizedEvent>.Success(parsed));
        _receiptRead.SetupSequence(x => x.FirstOrDefaultAsync(It.IsAny<BillingWebhookReceiptByProviderEventIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BillingWebhookReceipt?)null);
        _receiptWrite.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _activation.Setup(x => x.TryActivateAsync(sessionId, null, BillingProvider.Stripe, "ref_ok", BillingActivationSource.Webhook, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SubscriptionCheckoutSessionDto>.Success(activationDto));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(BillingProvider.Stripe), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Duplicate.Should().BeFalse();
        result.Value.Processed.Should().BeTrue();
        result.Value.ProviderEventId.Should().Be("evt_ok");
        _receiptWrite.Verify(x => x.AddAsync(It.IsAny<BillingWebhookReceipt>(), It.IsAny<CancellationToken>()), Times.Once);
        _receiptWrite.Verify(x => x.UpdateAsync(It.IsAny<BillingWebhookReceipt>(), It.IsAny<CancellationToken>()), Times.Once);
        _auditWriter.Verify(x => x.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_PaymentFailed_HasProviderMismatch()
    {
        var sessionId = Guid.NewGuid();
        var parsed = new BillingWebhookNormalizedEvent("evt_fail", "payment.failed", BillingWebhookEventKind.PaymentFailed, sessionId, "pay_ref");
        var session = BillingCheckoutSession.CreatePending(
            Guid.NewGuid(),
            SubscriptionPlanCode.Basic,
            SubscriptionPlanCode.Pro,
            BillingProvider.Stripe,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(20));

        _signatureVerifier.Setup(x => x.Verify(It.IsAny<BillingProvider>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Returns(Result.Success());
        _payloadParser.Setup(x => x.Parse(It.IsAny<BillingProvider>(), It.IsAny<string>()))
            .Returns(Result<BillingWebhookNormalizedEvent>.Success(parsed));
        _receiptRead.SetupSequence(x => x.FirstOrDefaultAsync(It.IsAny<BillingWebhookReceiptByProviderEventIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BillingWebhookReceipt?)null);
        _receiptWrite.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _sessionsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<BillingCheckoutSessionByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(BillingProvider.Iyzico), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Billing.ProviderMismatch");
        _sessionsWrite.Verify(x => x.UpdateAsync(It.IsAny<BillingCheckoutSession>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditWriter.Verify(x => x.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
