namespace Backend.Veteriner.Application.Ai;

/// <summary>
/// AI kullanım kayıtlarının toplamları (sınır denetimi ve platform raporu). Sistem toplamı kiracılar arası okunur
/// (ADR-044 madde 8: tüm sistem için aylık harcama sınırı); uygulaması Infrastructure'da.
/// </summary>
public interface IAiUsageReader
{
    /// <summary>Kullanıcının <paramref name="sinceUtc"/> sonrası istek sayısı (etkin kiracıda).</summary>
    Task<int> CountUserRequestsSinceAsync(Guid userId, DateTime sinceUtc, CancellationToken ct);

    /// <summary>Kliniğin aralıktaki toplamı (etkin kiracıda).</summary>
    Task<AiUsageTotals> ClinicTotalsAsync(Guid clinicId, DateTime fromUtc, DateTime toUtc, CancellationToken ct);

    /// <summary>Tüm kiracıların aralıktaki toplamı.</summary>
    Task<AiUsageTotals> SystemTotalsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct);

    /// <summary>Tüm kiracılarda klinik bazında toplam (platform yöneticisi raporu), maliyete göre azalan.</summary>
    Task<IReadOnlyList<AiClinicUsageRow>> ClinicBreakdownAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct);
}

public sealed record AiUsageTotals(int Requests, long Tokens, decimal CostUsd)
{
    public static readonly AiUsageTotals Zero = new(0, 0, 0m);
}

public sealed record AiClinicUsageRow(Guid TenantId, Guid ClinicId, string ClinicName, int Requests, long Tokens, decimal CostUsd);
