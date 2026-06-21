namespace Backend.Veteriner.Infrastructure.Persistence.Query.Models;

/// <summary>
/// Per-payment finance projection katkı durumu. Günlük aggregate recompute için SUM kaynağıdır;
/// <c>payment.updated</c> eski bucket'ı event'te <c>Previous</c> olmadığı için bu satırdan okunur.
/// </summary>
public sealed class PaymentDailyContributionReadModel
{
    public Guid PaymentId { get; set; }
    public Guid TenantId { get; set; }
    public Guid ClinicId { get; set; }
    public DateOnly LocalDate { get; set; }
    public string Currency { get; set; } = default!;
    public decimal Amount { get; set; }
    public Guid LastEventId { get; set; }
    public DateTime LastEventOccurredAtUtc { get; set; }
    public DateTime LastProjectedAtUtc { get; set; }
}
