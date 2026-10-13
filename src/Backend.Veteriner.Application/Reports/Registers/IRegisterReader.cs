namespace Backend.Veteriner.Application.Reports.Registers;

/// <summary>
/// Defter satırı okuyucusu (Command DB). Süzgeç, sıralama, sayım ve sayfalama veritabanında yapılır;
/// JSON rapor ve dışa aktarma aynı çağrıyı kullanır. <c>RowNumber</c> okuyucuda <c>Skip</c> + konum + 1 olarak verilir.
/// </summary>
public interface IRegisterReader<TRow>
{
    Task<RegisterReadResult<TRow>> ReadAsync(RegisterReadRequest request, CancellationToken ct = default);
}

/// <param name="ClinicId">Tek klinik kapsamı; null ise <paramref name="AccessibleClinicIds"/> (null = kiracı geneli).</param>
/// <param name="FromUtc">Dahil.</param>
/// <param name="ToUtcExclusive">Hariç (İstanbul gün sınırı).</param>
/// <param name="IncludeVoided">İptal edilmiş uygulamalar da gelsin (yalnız muayene defteri).</param>
public sealed record RegisterReadRequest(
    Guid TenantId,
    Guid? ClinicId,
    IReadOnlyCollection<Guid>? AccessibleClinicIds,
    DateTime FromUtc,
    DateTime ToUtcExclusive,
    bool IncludeVoided,
    int Skip,
    int Take);

/// <param name="TotalCount">Süzgece uyan kayıt sayısı (tüm sayfalar).</param>
public sealed record RegisterReadResult<TRow>(int TotalCount, IReadOnlyList<TRow> Items);
