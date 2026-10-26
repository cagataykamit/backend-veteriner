namespace Backend.Veteriner.Application.Outbox;

/// <summary><see cref="IOutboxAdminRepository.RetryDeadLetterAsync"/> sonucu.</summary>
public enum OutboxRetryOutcome
{
    Retried,
    NotFound,
    NotDeadLetter
}
