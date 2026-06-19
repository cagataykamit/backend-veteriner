namespace Backend.Veteriner.Infrastructure.Persistence.Query.Models;

/// <summary>
/// Client list/search için CQRS query read-model (tenant kapsamlı).
/// Bu faz yalnızca şema temelidir; projection/event emission ileride eklenecektir.
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
}
