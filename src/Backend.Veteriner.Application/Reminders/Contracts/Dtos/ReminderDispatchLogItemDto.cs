using Backend.Veteriner.Domain.Reminders;

namespace Backend.Veteriner.Application.Reminders.Contracts.Dtos;

public sealed record ReminderDispatchLogItemDto(
    Guid Id,
    Guid? ClinicId,
    ReminderType ReminderType,
    ReminderSourceEntityType SourceEntityType,
    Guid SourceEntityId,
    string RecipientEmail,
    string RecipientName,
    DateTime ScheduledForUtc,
    DateTime ReminderDueAtUtc,
    ReminderDispatchStatus Status,
    Guid? OutboxMessageId,
    DateTime? SentAtUtc,
    DateTime? FailedAtUtc,
    string? LastError,
    DateTime CreatedAtUtc);
