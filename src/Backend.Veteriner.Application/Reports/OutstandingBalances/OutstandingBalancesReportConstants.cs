namespace Backend.Veteriner.Application.Reports.OutstandingBalances;

/// <summary>Borç raporu sabitleri; sayısal tavanlar <see cref="ReportsSharedLimits"/> ile hizalıdır. Tarih aralığı yoktur (şu anki kalan).</summary>
public static class OutstandingBalancesReportConstants
{
    public const int MaxPageSize = ReportsSharedLimits.MaxPageSize;

    public const int DefaultPageSize = 50;

    public const int MaxExportRows = ReportsSharedLimits.MaxExportRows;
}
