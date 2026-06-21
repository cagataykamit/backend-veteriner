namespace Backend.Veteriner.Application.Payments.ReadModels;

/// <summary>
/// Command DB <c>Payments</c> ile Query DB <c>PaymentReadModels</c> (list read-model) parity sonucu (CQRS-14F).
/// Tenant + clinic kapsamlıdır (list read yüzeyi ile aynı kapsam). PII içermez.
///
/// Üç boyut:
/// - <see cref="CountInSync"/>: tenant+clinic toplam ödeme sayısı.
/// - <see cref="RowSampleParityInSync"/>: recent top-N örneğinde kritik alanların eşitliği.
/// - <see cref="RecentOrderingInSync"/>: PaidAtUtc DESC, PaymentId DESC sıralı ilk N kaydın aynı sırada olması.
/// </summary>
public sealed record PaymentReadModelParityResult(
    long CommandCount,
    long QueryCount,
    bool CountInSync,
    bool RowSampleParityInSync,
    int RowSampleMismatchCount,
    IReadOnlyList<PaymentReadModelRowMismatch> RowSampleMismatches,
    bool RecentOrderingInSync,
    Guid TenantId,
    Guid ClinicId)
{
    /// <summary>Command - Query. Pozitif değer read-model'in geride kaldığını gösterir.</summary>
    public long CountDifference => CommandCount - QueryCount;

    public long AbsoluteCountDifference => Math.Abs(CountDifference);

    public bool InSync => CountInSync && RowSampleParityInSync && RecentOrderingInSync;
}
