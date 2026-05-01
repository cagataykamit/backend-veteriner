namespace Backend.Veteriner.Infrastructure.Reminders;

public interface IReminderEmailOutboxEnqueuer
{
    Task<Guid> EnqueueReminderEmailAsync(
        string to,
        string subject,
        string body,
        bool isHtml,
        CancellationToken ct);
}

