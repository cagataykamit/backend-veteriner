using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Tenants;

public sealed class ScheduledSubscriptionPlanChange : AggregateRoot
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public SubscriptionPlanCode CurrentPlanCode { get; private set; }
    public SubscriptionPlanCode TargetPlanCode { get; private set; }
    public SubscriptionPlanChangeType ChangeType { get; private set; }
    public SubscriptionPlanChangeStatus Status { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public DateTime EffectiveAtUtc { get; private set; }
    public DateTime? AppliedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? Reason { get; private set; }

    private ScheduledSubscriptionPlanChange() { }

    public static ScheduledSubscriptionPlanChange CreateScheduledDowngrade(
        Guid tenantId,
        SubscriptionPlanCode currentPlanCode,
        SubscriptionPlanCode targetPlanCode,
        Guid requestedByUserId,
        DateTime requestedAtUtc,
        DateTime effectiveAtUtc,
        string? reason)
    {
        var now = NormalizeUtc(requestedAtUtc);
        var effective = NormalizeUtc(effectiveAtUtc);
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (requestedByUserId == Guid.Empty) throw new ArgumentException("RequestedByUserId geçersiz.", nameof(requestedByUserId));
        if (effective <= now) throw new ArgumentException("EffectiveAtUtc gelecekte olmalı.", nameof(effectiveAtUtc));
        if (currentPlanCode == targetPlanCode) throw new ArgumentException("Aynı plan için değişiklik açılamaz.");

        return new ScheduledSubscriptionPlanChange
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CurrentPlanCode = currentPlanCode,
            TargetPlanCode = targetPlanCode,
            ChangeType = SubscriptionPlanChangeType.Downgrade,
            Status = SubscriptionPlanChangeStatus.Scheduled,
            RequestedByUserId = requestedByUserId,
            RequestedAtUtc = now,
            EffectiveAtUtc = effective,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
        };
    }

    public void Cancel(DateTime utcNow)
    {
        if (Status != SubscriptionPlanChangeStatus.Scheduled)
            return;

        Status = SubscriptionPlanChangeStatus.Cancelled;
        CancelledAtUtc = NormalizeUtc(utcNow);
    }

    public void MarkApplied(DateTime utcNow)
    {
        if (Status != SubscriptionPlanChangeStatus.Scheduled)
            return;

        Status = SubscriptionPlanChangeStatus.Applied;
        AppliedAtUtc = NormalizeUtc(utcNow);
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
