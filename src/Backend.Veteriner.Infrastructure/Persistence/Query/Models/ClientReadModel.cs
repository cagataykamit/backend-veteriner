namespace Backend.Veteriner.Infrastructure.Persistence.Query.Models;

/// <summary>
/// Client list/search için CQRS query read-model (tenant kapsamlı).
/// CQRS-12B-3: Client projection processor bu satırları integration event'lerden upsert eder.
/// </summary>
public sealed class ClientReadModel
{
    public Guid ClientId { get; set; }
    public Guid TenantId { get; set; }
    public string FullName { get; set; } = default!;
    public string FullNameNormalized { get; set; } = default!;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? PhoneNormalized { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public Guid LastEventId { get; set; }
    public DateTime LastProjectedAtUtc { get; set; }

    /// <summary>
    /// Bu satırı en son güncelleyen integration event'in <c>OccurredAtUtc</c> değeri.
    /// Client event'lerinde per-aggregate sequence olmadığından stale/out-of-order korumasının
    /// ordering anahtarıdır: daha eski OccurredAtUtc taşıyan event mevcut veriyi ezmez.
    /// <see cref="LastProjectedAtUtc"/> (projection wall-clock) ile karıştırılmamalıdır.
    /// </summary>
    public DateTime LastEventOccurredAtUtc { get; set; }
}
