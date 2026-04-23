namespace Backend.Veteriner.Application.Reports;

/// <summary>
/// Ödeme, randevu, muayene ve aşı raporlarında paylaşılan üst sınırlar (Faz 6D).
/// Modül başına <c>*ReportConstants</c> bu değerlere alias verir; iş kuralları ayrı hata kodlarıyla kalır.
/// </summary>
public static class ReportsSharedLimits
{
    /// <summary>Kapalı <c>[from,to]</c> aralığı için maksimum <c>(to - from)</c> gün sayısı.</summary>
    public const int MaxRangeDays = 93;

    /// <summary>JSON rapor sayfa boyutu üst sınırı.</summary>
    public const int MaxPageSize = 200;

    /// <summary>CSV/XLSX export için tek yanıtta satır üst güvenlik tavanı.</summary>
    public const int MaxExportRows = 50_000;
}
