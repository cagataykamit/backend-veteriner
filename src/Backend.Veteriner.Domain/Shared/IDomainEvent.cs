namespace Backend.Veteriner.Domain.Shared;

/// <summary>
/// Domain event işaretleyicisi. Yayın altyapısı (outbox köprüsü) park edilmiştir; bkz. foundation-rules §9.
/// Domain dış pakete bağımlı olmadığı için burada Mediator bildirimi türetilmez.
/// </summary>
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTime OccurredOnUtc { get; }
}
