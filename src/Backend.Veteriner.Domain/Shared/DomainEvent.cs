namespace Backend.Veteriner.Domain.Shared;

/// <summary>
/// Tüm domain event'ler için temel sınıf.
/// </summary>
public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;
}