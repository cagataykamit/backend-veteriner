using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Tenants;

public sealed class BillingCheckoutSession : AggregateRoot
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public SubscriptionPlanCode CurrentPlanCode { get; private set; }
    public SubscriptionPlanCode TargetPlanCode { get; private set; }
    public BillingCheckoutSessionStatus Status { get; private set; }
    public BillingProvider Provider { get; private set; }
    public string? ExternalReference { get; private set; }
    public string? CheckoutUrl { get; private set; }
    public DateTime? ExpiresAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime? FailedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    private BillingCheckoutSession() { }

    public static BillingCheckoutSession CreatePending(
        Guid tenantId,
        SubscriptionPlanCode currentPlanCode,
        SubscriptionPlanCode targetPlanCode,
        BillingProvider provider,
        DateTime utcNow,
        DateTime? expiresAtUtc)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));

        var now = NormalizeUtc(utcNow);
        return new BillingCheckoutSession
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CurrentPlanCode = currentPlanCode,
            TargetPlanCode = targetPlanCode,
            Status = BillingCheckoutSessionStatus.Pending,
            Provider = provider,
            ExpiresAtUtc = expiresAtUtc.HasValue ? NormalizeUtc(expiresAtUtc.Value) : null,
            CreatedAtUtc = now,
            UpdatedAtUtc = null,
        };
    }

    public bool IsOpen(DateTime utcNow)
    {
        var now = NormalizeUtc(utcNow);
        if (ExpiresAtUtc.HasValue && ExpiresAtUtc.Value <= now)
            return false;

        return Status is BillingCheckoutSessionStatus.Pending or BillingCheckoutSessionStatus.RedirectReady;
    }

    public void SetRedirectReady(string? checkoutUrl, string? externalReference, DateTime utcNow)
    {
        EnsureMutableOpen();
        CheckoutUrl = string.IsNullOrWhiteSpace(checkoutUrl) ? null : checkoutUrl.Trim();
        ExternalReference = string.IsNullOrWhiteSpace(externalReference) ? null : externalReference.Trim();
        Status = BillingCheckoutSessionStatus.RedirectReady;
        UpdatedAtUtc = NormalizeUtc(utcNow);
    }

    public void MarkCompleted(DateTime utcNow, string? externalReference = null)
    {
        EnsureMutableOpen();
        Status = BillingCheckoutSessionStatus.Completed;
        CompletedAtUtc = NormalizeUtc(utcNow);
        UpdatedAtUtc = CompletedAtUtc;
        if (!string.IsNullOrWhiteSpace(externalReference))
            ExternalReference = externalReference.Trim();
    }

    public void MarkFailed(DateTime utcNow, string? externalReference = null)
    {
        EnsureMutableOpen();
        Status = BillingCheckoutSessionStatus.Failed;
        FailedAtUtc = NormalizeUtc(utcNow);
        UpdatedAtUtc = FailedAtUtc;
        if (!string.IsNullOrWhiteSpace(externalReference))
            ExternalReference = externalReference.Trim();
    }

    public void MarkExpired(DateTime utcNow)
    {
        if (Status is BillingCheckoutSessionStatus.Completed
            or BillingCheckoutSessionStatus.Failed
            or BillingCheckoutSessionStatus.Cancelled
            or BillingCheckoutSessionStatus.Expired)
            return;

        Status = BillingCheckoutSessionStatus.Expired;
        UpdatedAtUtc = NormalizeUtc(utcNow);
    }

    public void MarkCancelled(DateTime utcNow)
    {
        if (Status is BillingCheckoutSessionStatus.Completed
            or BillingCheckoutSessionStatus.Failed
            or BillingCheckoutSessionStatus.Cancelled
            or BillingCheckoutSessionStatus.Expired)
            return;

        Status = BillingCheckoutSessionStatus.Cancelled;
        UpdatedAtUtc = NormalizeUtc(utcNow);
    }

    private void EnsureMutableOpen()
    {
        if (Status is BillingCheckoutSessionStatus.Completed
            or BillingCheckoutSessionStatus.Failed
            or BillingCheckoutSessionStatus.Cancelled
            or BillingCheckoutSessionStatus.Expired)
        {
            throw new InvalidOperationException("Bu checkout session kapanmış; durum değiştirilemez.");
        }
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}

