using Ardalis.Specification;
using Backend.Veteriner.Application.Reminders.Contracts.Dtos;
using Backend.Veteriner.Domain.Reminders;

namespace Backend.Veteriner.Application.Reminders.Specs;

public sealed class ReminderDispatchLogsCountSpec : Specification<ReminderDispatchLog>
{
    public ReminderDispatchLogsCountSpec(
        Guid tenantId,
        ReminderType? reminderType,
        ReminderDispatchStatus? status,
        DateTime? fromUtc,
        DateTime? toUtc)
    {
        Query.AsNoTracking();
        Query.Where(x => x.TenantId == tenantId);
        if (reminderType.HasValue)
            Query.Where(x => x.ReminderType == reminderType.Value);
        if (status.HasValue)
            Query.Where(x => x.Status == status.Value);
        if (fromUtc.HasValue)
            Query.Where(x => x.CreatedAtUtc >= fromUtc.Value);
        if (toUtc.HasValue)
            Query.Where(x => x.CreatedAtUtc <= toUtc.Value);
    }
}

public sealed class ReminderDispatchLogsFilteredPagedSpec : Specification<ReminderDispatchLog, ReminderDispatchLogItemDto>
{
    public ReminderDispatchLogsFilteredPagedSpec(
        Guid tenantId,
        ReminderType? reminderType,
        ReminderDispatchStatus? status,
        DateTime? fromUtc,
        DateTime? toUtc,
        int page,
        int pageSize)
    {
        Query.AsNoTracking();
        Query.Where(x => x.TenantId == tenantId);
        if (reminderType.HasValue)
            Query.Where(x => x.ReminderType == reminderType.Value);
        if (status.HasValue)
            Query.Where(x => x.Status == status.Value);
        if (fromUtc.HasValue)
            Query.Where(x => x.CreatedAtUtc >= fromUtc.Value);
        if (toUtc.HasValue)
            Query.Where(x => x.CreatedAtUtc <= toUtc.Value);

        Query.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id);
        Query.Skip((page - 1) * pageSize).Take(pageSize);
        Query.Select(x => new ReminderDispatchLogItemDto(
            x.Id,
            x.ClinicId,
            x.ReminderType,
            x.SourceEntityType,
            x.SourceEntityId,
            x.RecipientEmail,
            x.RecipientName,
            x.ScheduledForUtc,
            x.ReminderDueAtUtc,
            x.Status,
            x.OutboxMessageId,
            x.SentAtUtc,
            x.FailedAtUtc,
            x.LastError,
            x.CreatedAtUtc));
    }
}
