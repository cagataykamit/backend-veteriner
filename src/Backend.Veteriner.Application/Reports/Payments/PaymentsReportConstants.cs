namespace Backend.Veteriner.Application.Reports.Payments;

/// <summary>Ödeme raporu / CSV export tarih aralığı üst sınırı (gün). Dashboard Operasyon pencerelerinden bağımsızdır.</summary>
public static class PaymentsReportConstants
{
    /// <summary>Maksimum kapanmış aralık süresi (dahil uçlar): <c>(to - from)</c> ≤ bu değer.</summary>
    public const int MaxRangeDays = 93;

    public const int MaxPageSize = 200;
    public const int DefaultPageSize = 50;

    /// <summary>Export için tek yanıtta satır üst güvenlik tavanı (aşılırsa 400).</summary>
    public const int MaxExportRows = 50_000;
}
