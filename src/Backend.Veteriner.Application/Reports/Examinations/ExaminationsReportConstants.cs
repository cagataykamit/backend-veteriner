namespace Backend.Veteriner.Application.Reports.Examinations;

/// <summary>Muayene raporu tarih aralığı ve export tavanı (dashboard ile karıştırılmaz).</summary>
public static class ExaminationsReportConstants
{
    public const int MaxRangeDays = 93;

    public const int MaxPageSize = 200;

    /// <summary>CSV/XLSX export satır üst sınırı (ödeme/randevu raporu ile aynı).</summary>
    public const int MaxExportRows = 50_000;
}
