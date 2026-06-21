namespace Backend.Veteriner.Infrastructure.Persistence.Query.Models;

/// <summary>
/// Klinik + gün + para birimi bazında ödenen tahsilat istatistikleri (dashboard finance projection hedefi).
/// 13B: tablo/migration; 13C: projection processor doldurur.
/// </summary>
public sealed class ClinicDailyPaymentStatsReadModel
{
    public Guid TenantId { get; set; }
    public Guid ClinicId { get; set; }
    public DateOnly LocalDate { get; set; }
    public string Currency { get; set; } = default!;
    public decimal PaidTotalAmount { get; set; }
    public int PaidCount { get; set; }
    public Guid LastEventId { get; set; }
    public DateTime LastEventOccurredAtUtc { get; set; }
    public DateTime LastProjectedAtUtc { get; set; }
}
