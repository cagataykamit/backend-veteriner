using Backend.Veteriner.Domain.Ai;

namespace Backend.Veteriner.Application.Ai;

/// <summary>
/// AI kullanım kayıtlarının toplamları (sınır denetimi, kredi kotası ve platform raporu). Sistem toplamı ve platform raporu
/// kiracılar arası okunur (ADR-044 madde 8: tüm sistem için aylık harcama sınırı); uygulaması Infrastructure'da.
/// </summary>
public interface IAiUsageReader
{
    /// <summary>Kullanıcının <paramref name="sinceUtc"/> sonrası istek sayısı (etkin kiracıda).</summary>
    Task<int> CountUserRequestsSinceAsync(Guid userId, DateTime sinceUtc, CancellationToken ct);

    /// <summary>Kliniğin kredi ve sigorta toplamları (etkin kiracıda): ayın kullanımı ve tüm zamanların fon hareketi.</summary>
    Task<AiClinicCreditTotals> ClinicCreditTotalsAsync(Guid clinicId, DateTime monthStartUtc, DateTime monthEndUtc, CancellationToken ct);

    /// <summary>Platform yönetimi: birden çok kliniğin (kiracılar arası) aynı toplamları; kaydı olmayan klinik listede yoktur.</summary>
    Task<IReadOnlyDictionary<Guid, AiClinicCreditTotals>> ClinicsCreditTotalsAsync(
        IReadOnlyCollection<Guid> clinicIds, DateTime monthStartUtc, DateTime monthEndUtc, CancellationToken ct);

    /// <summary>Tüm kiracıların aralıktaki toplamı.</summary>
    Task<AiUsageTotals> SystemTotalsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct);

    /// <summary>Platform yönetimi (AI-008): aralıkta sistem toplamı ve klinik, özellik, model kırılımı (kiracılar arası).</summary>
    Task<AiUsageBreakdown> BreakdownAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct);
}

public sealed record AiUsageTotals(int Requests, long Tokens, decimal CostUsd)
{
    public static readonly AiUsageTotals Zero = new(0, 0, 0m);
}

/// <summary>Kliniğin kredi hesabına giren toplamlar; bakiye ve oranlar <see cref="AiClinicQuota"/>'da hesaplanır.</summary>
/// <param name="MonthCredits">Bu ay harcanan kredi (kota + fon).</param>
/// <param name="MonthFundCredits">Bu ay fondan karşılanan kısım.</param>
/// <param name="FundGranted">Tüm zamanlarda eklenen fon.</param>
/// <param name="FundUsed">Tüm zamanlarda fondan harcanan.</param>
public sealed record AiClinicCreditTotals(
    long MonthTokens,
    decimal MonthCostUsd,
    decimal MonthCredits,
    decimal MonthFundCredits,
    decimal FundGranted,
    decimal FundUsed)
{
    public static readonly AiClinicCreditTotals Zero = new(0, 0m, 0m, 0m, 0m, 0m);
}

/// <summary>Bir kırılım satırının ölçüleri (gerçek token, kredi, tahmini maliyet; klinik metin içermez).</summary>
public sealed record AiUsageStats(
    int Requests,
    int FailedRequests,
    long InputTokens,
    long OutputTokens,
    decimal Credits,
    decimal FundCredits,
    decimal CostUsd)
{
    public static readonly AiUsageStats Zero = new(0, 0, 0, 0, 0m, 0m, 0m);

    public long Tokens => InputTokens + OutputTokens;
}

public sealed record AiClinicUsageRow(Guid TenantId, string TenantName, Guid ClinicId, string ClinicName, AiUsageStats Stats);

public sealed record AiFeatureUsageRow(AiFeature Feature, AiUsageStats Stats);

public sealed record AiModelUsageRow(string Model, AiUsageStats Stats);

/// <summary>Satırlar maliyete göre azalan.</summary>
public sealed record AiUsageBreakdown(
    AiUsageStats Total,
    IReadOnlyList<AiClinicUsageRow> Clinics,
    IReadOnlyList<AiFeatureUsageRow> Features,
    IReadOnlyList<AiModelUsageRow> Models);
