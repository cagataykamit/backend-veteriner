namespace Backend.Veteriner.Infrastructure.Persistence.Query.Models;

/// <summary>
/// Payment list/search/recent için CQRS query read-model (tenant + clinic kapsamlı).
/// CQRS-14B: tablo/migration; 14C: projection processor doldurur.
/// </summary>
public sealed class PaymentReadModel
{
    public Guid PaymentId { get; set; }
    public Guid TenantId { get; set; }
    public Guid ClinicId { get; set; }

    /// <summary>
    /// Denormalize klinik adı (CQRS-15D). Client payment summary / report-export gibi yüzeyler için display alanıdır.
    /// ClientName ile aynı pattern: zorunlu (non-null); 15D öncesi payload'larda projection/backfill defensive
    /// fallback ile boş string yazar. Klinik adı ile filtre/search yapılmadığından normalize alan eklenmemiştir.
    /// </summary>
    public string ClinicName { get; set; } = default!;
    public Guid ClientId { get; set; }
    public string ClientName { get; set; } = default!;
    public string ClientNameNormalized { get; set; } = default!;
    public Guid? PetId { get; set; }
    public string? PetName { get; set; }
    public string? PetNameNormalized { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = default!;
    public int Method { get; set; }
    public DateTime PaidAtUtc { get; set; }
    public string? Notes { get; set; }
    public string? NotesNormalized { get; set; }
    public Guid? AppointmentId { get; set; }
    public Guid? ExaminationId { get; set; }
    public Guid LastEventId { get; set; }
    public DateTime LastEventOccurredAtUtc { get; set; }
    public DateTime LastProjectedAtUtc { get; set; }
}
