using System.Text.Json;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Auditing;
using Backend.Veteriner.Application.Common.Billing;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.Tenants.Commands.Billing;

public sealed class ProcessBillingWebhookCommandHandler
    : IRequestHandler<ProcessBillingWebhookCommand, Result<BillingWebhookAckDto>>
{
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    private readonly IBillingWebhookSignatureVerifier _signatureVerifier;
    private readonly IBillingWebhookPayloadParser _payloadParser;
    private readonly ISubscriptionCheckoutActivationService _activation;
    private readonly IReadRepository<BillingWebhookReceipt> _receiptRead;
    private readonly IRepository<BillingWebhookReceipt> _receiptWrite;
    private readonly IReadRepository<BillingCheckoutSession> _sessionsRead;
    private readonly IRepository<BillingCheckoutSession> _sessionsWrite;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly IClientContext _clientContext;
    private readonly ILogger<ProcessBillingWebhookCommandHandler> _logger;

    public ProcessBillingWebhookCommandHandler(
        IBillingWebhookSignatureVerifier signatureVerifier,
        IBillingWebhookPayloadParser payloadParser,
        ISubscriptionCheckoutActivationService activation,
        IReadRepository<BillingWebhookReceipt> receiptRead,
        IRepository<BillingWebhookReceipt> receiptWrite,
        IReadRepository<BillingCheckoutSession> sessionsRead,
        IRepository<BillingCheckoutSession> sessionsWrite,
        IAuditLogWriter auditLogWriter,
        IClientContext clientContext,
        ILogger<ProcessBillingWebhookCommandHandler> logger)
    {
        _signatureVerifier = signatureVerifier;
        _payloadParser = payloadParser;
        _activation = activation;
        _receiptRead = receiptRead;
        _receiptWrite = receiptWrite;
        _sessionsRead = sessionsRead;
        _sessionsWrite = sessionsWrite;
        _auditLogWriter = auditLogWriter;
        _clientContext = clientContext;
        _logger = logger;
    }

    public async Task<Result<BillingWebhookAckDto>> Handle(ProcessBillingWebhookCommand request, CancellationToken ct)
    {
        var verified = _signatureVerifier.Verify(request.Provider, request.RawBody, request.Headers);
        if (!verified.IsSuccess)
        {
            _logger.LogWarning(
                "Billing webhook imza doğrulaması başarısız: {Provider} {Code} {Message}",
                request.Provider,
                verified.Error.Code,
                verified.Error.Message);
            return Result<BillingWebhookAckDto>.Failure(verified.Error);
        }

        var parsed = _payloadParser.Parse(request.Provider, request.RawBody);
        if (!parsed.IsSuccess)
        {
            _logger.LogWarning(
                "Billing webhook payload ayrıştırılamadı: {Provider} {Code} {Message}",
                request.Provider,
                parsed.Error.Code,
                parsed.Error.Message);
            return Result<BillingWebhookAckDto>.Failure(parsed.Error);
        }

        var ev = parsed.Value!;
        var utcNow = DateTime.UtcNow;

        _logger.LogInformation(
            "Billing webhook normalize edildi: {Provider} Kind={Kind} EventType={EventType} EventId={EventId} CheckoutSessionId={CheckoutSessionId}",
            request.Provider,
            ev.Kind,
            ev.EventType,
            ev.ProviderEventId,
            ev.BillingCheckoutSessionId);

        var receipt = await _receiptRead.FirstOrDefaultAsync(
            new BillingWebhookReceiptByProviderEventIdSpec(request.Provider, ev.ProviderEventId), ct);

        if (receipt?.ProcessedAtUtc is not null)
        {
            _logger.LogInformation(
                "Billing webhook duplicate (already processed): {Provider} {EventId}",
                request.Provider,
                ev.ProviderEventId);
            return Result<BillingWebhookAckDto>.Success(new BillingWebhookAckDto(true, false, ev.ProviderEventId));
        }

        if (receipt is null)
        {
            var created = BillingWebhookReceipt.CreateReceived(
                request.Provider,
                ev.ProviderEventId,
                ev.EventType,
                ev.BillingCheckoutSessionId,
                correlationId: _clientContext.CorrelationId,
                utcNow);

            await _receiptWrite.AddAsync(created, ct);
            try
            {
                await _receiptWrite.SaveChangesAsync(ct);
                receipt = created;
            }
            catch (DbUpdateException ex) when (IsDuplicateKeyViolation(ex))
            {
                receipt = await _receiptRead.FirstOrDefaultAsync(
                    new BillingWebhookReceiptByProviderEventIdSpec(request.Provider, ev.ProviderEventId), ct);
                if (receipt?.ProcessedAtUtc is not null)
                    return Result<BillingWebhookAckDto>.Success(new BillingWebhookAckDto(true, false, ev.ProviderEventId));
            }
        }

        if (receipt is null)
        {
            return Result<BillingWebhookAckDto>.Failure(
                "Billing.WebhookReceiptPersistFailed",
                "Webhook alındı ancak idempotency kaydı oluşturulamadı.");
        }

        var business = await ProcessNormalizedAsync(request.Provider, ev, utcNow, ct);
        if (!business.IsSuccess)
            return Result<BillingWebhookAckDto>.Failure(business.Error);

        receipt.MarkProcessed(utcNow);
        await _receiptWrite.UpdateAsync(receipt, ct);
        await _receiptWrite.SaveChangesAsync(ct);

        var auditPayload = JsonSerializer.Serialize(
            new
            {
                Provider = request.Provider.ToString(),
                ev.ProviderEventId,
                ev.EventType,
                Kind = ev.Kind.ToString(),
                ev.BillingCheckoutSessionId,
                Duplicate = false,
                Processed = true,
            },
            AuditJson);

        await _auditLogWriter.WriteAsync(
            new AuditLogEntry(
                ActorUserId: _clientContext.UserId,
                Action: "Billing.WebhookProcessed",
                TargetType: nameof(BillingWebhookReceipt),
                TargetId: receipt.Id.ToString(),
                Success: true,
                FailureReason: null,
                Route: _clientContext.Path,
                HttpMethod: _clientContext.Method,
                IpAddress: _clientContext.IpAddress,
                UserAgent: _clientContext.UserAgent,
                CorrelationId: _clientContext.CorrelationId,
                RequestName: nameof(ProcessBillingWebhookCommand),
                RequestPayload: auditPayload,
                OccurredAtUtc: utcNow),
            ct);

        return Result<BillingWebhookAckDto>.Success(new BillingWebhookAckDto(false, true, ev.ProviderEventId));
    }

    private async Task<Result> ProcessNormalizedAsync(
        BillingProvider provider,
        BillingWebhookNormalizedEvent ev,
        DateTime utcNow,
        CancellationToken ct)
    {
        return ev.Kind switch
        {
            BillingWebhookEventKind.Ignored => Result.Success(),
            BillingWebhookEventKind.PaymentSucceeded => await ProcessPaymentSucceededAsync(provider, ev, ct),
            BillingWebhookEventKind.PaymentFailed => await ProcessPaymentFailedAsync(provider, ev, utcNow, ct),
            _ => Result.Success(),
        };
    }

    private async Task<Result> ProcessPaymentSucceededAsync(
        BillingProvider provider,
        BillingWebhookNormalizedEvent ev,
        CancellationToken ct)
    {
        if (!ev.BillingCheckoutSessionId.HasValue)
        {
            return Result.Failure(
                "Billing.WebhookCheckoutSessionMissing",
                "Ödeme başarılı ancak checkout session kimliği (metadata) yok.");
        }

        var activation = await _activation.TryActivateAsync(
            ev.BillingCheckoutSessionId.Value,
            tenantIdConstraint: null,
            providerMustMatch: provider,
            ev.ProviderPaymentReference,
            BillingActivationSource.Webhook,
            ct);

        if (!activation.IsSuccess)
        {
            _logger.LogWarning(
                "Billing webhook ödeme başarılı ancak aktivasyon başarısız: {Code} {Message} SessionId={SessionId}",
                activation.Error.Code,
                activation.Error.Message,
                ev.BillingCheckoutSessionId);
        }

        return activation.IsSuccess ? Result.Success() : Result.Failure(activation.Error);
    }

    private async Task<Result> ProcessPaymentFailedAsync(
        BillingProvider provider,
        BillingWebhookNormalizedEvent ev,
        DateTime utcNow,
        CancellationToken ct)
    {
        if (!ev.BillingCheckoutSessionId.HasValue)
            return Result.Success();

        var session = await _sessionsRead.FirstOrDefaultAsync(
            new BillingCheckoutSessionByIdSpec(ev.BillingCheckoutSessionId.Value), ct);
        if (session is null)
            return Result.Success();

        if (session.Provider != provider)
        {
            return Result.Failure(
                "Billing.ProviderMismatch",
                "Checkout session bu ödeme sağlayıcısı ile oluşturulmamış.");
        }

        var failureReference = provider == BillingProvider.Iyzico ? null : ev.ProviderPaymentReference;
        session.TryMarkFailedIfOpen(utcNow, failureReference);
        await _sessionsWrite.UpdateAsync(session, ct);
        await _sessionsWrite.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static bool IsDuplicateKeyViolation(DbUpdateException ex)
    {
        for (var e = ex.InnerException; e != null; e = e.InnerException)
        {
            if (e.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
                return true;
            if (e.Message.Contains("UNIQUE KEY constraint", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
