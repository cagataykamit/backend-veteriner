namespace Backend.Veteriner.Infrastructure.Persistence.Query.Models;

public sealed class ProcessedProjectionEvent
{
    public Guid EventId { get; set; }
    public string ConsumerName { get; set; } = default!;
    public DateTime ProcessedAtUtc { get; set; }
}
