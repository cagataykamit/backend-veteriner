using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Reminders;

public sealed class ReminderDispatchLog : AggregateRoot
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid? ClinicId { get; private set; }
    public ReminderType ReminderType { get; private set; }
    public ReminderSourceEntityType SourceEntityType { get; private set; }
    public Guid SourceEntityId { get; private set; }
    public string RecipientEmail { get; private set; } = default!;
    public string RecipientName { get; private set; } = default!;
    public DateTime ScheduledForUtc { get; private set; }
    public DateTime ReminderDueAtUtc { get; private set; }
    public ReminderDispatchStatus Status { get; private set; }
    public string DedupeKey { get; private set; } = default!;
    public Guid? OutboxMessageId { get; private set; }
    public DateTime? SentAtUtc { get; private set; }
    public DateTime? FailedAtUtc { get; private set; }
    public string? LastError { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    private ReminderDispatchLog() { }

    public ReminderDispatchLog(
        Guid tenantId,
        Guid? clinicId,
        ReminderType reminderType,
        ReminderSourceEntityType sourceEntityType,
        Guid sourceEntityId,
        string recipientEmail,
        string recipientName,
        DateTime scheduledForUtc,
        DateTime reminderDueAtUtc,
        ReminderDispatchStatus status,
        string dedupeKey)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (sourceEntityId == Guid.Empty)
            throw new ArgumentException("SourceEntityId geçersiz.", nameof(sourceEntityId));
        if (string.IsNullOrWhiteSpace(dedupeKey))
            throw new ArgumentException("DedupeKey boş olamaz.", nameof(dedupeKey));

        TenantId = tenantId;
        ClinicId = clinicId;
        ReminderType = reminderType;
        SourceEntityType = sourceEntityType;
        SourceEntityId = sourceEntityId;
        RecipientEmail = string.IsNullOrWhiteSpace(recipientEmail)
            ? string.Empty
            : recipientEmail.Trim().ToLowerInvariant();
        RecipientName = string.IsNullOrWhiteSpace(recipientName) ? string.Empty : recipientName.Trim();
        ScheduledForUtc = NormalizeUtc(scheduledForUtc);
        ReminderDueAtUtc = NormalizeUtc(reminderDueAtUtc);
        Status = status;
        DedupeKey = dedupeKey.Trim();
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = null;
    }

    public void MarkEnqueued(Guid? outboxMessageId = null)
    {
        Status = ReminderDispatchStatus.Enqueued;
        OutboxMessageId = outboxMessageId;
        SentAtUtc = null;
        LastError = null;
        FailedAtUtc = null;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkSent(DateTime sentAtUtc)
    {
        Status = ReminderDispatchStatus.Sent;
        SentAtUtc = NormalizeUtc(sentAtUtc);
        FailedAtUtc = null;
        LastError = null;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkFailed(string? error)
    {
        Status = ReminderDispatchStatus.Failed;
        LastError = string.IsNullOrWhiteSpace(error) ? "Unknown reminder enqueue error." : error.Trim();
        FailedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkFailed(DateTime failedAtUtc, string? error)
    {
        Status = ReminderDispatchStatus.Failed;
        FailedAtUtc = NormalizeUtc(failedAtUtc);
        LastError = string.IsNullOrWhiteSpace(error) ? "Unknown reminder send error." : error.Trim();
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkSkipped(string reason)
    {
        Status = ReminderDispatchStatus.Skipped;
        LastError = string.IsNullOrWhiteSpace(reason) ? "Reminder skipped." : reason.Trim();
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
