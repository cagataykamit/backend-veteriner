using Backend.Veteriner.Application.Reports;

namespace Backend.Veteriner.Application.Reports.Payments;

/// <summary>Ödeme raporu sabitleri; sayısal tavanlar <see cref="ReportsSharedLimits"/> ile hizalıdır.</summary>
public static class PaymentsReportConstants
{
    public const int MaxRangeDays = ReportsSharedLimits.MaxRangeDays;

    public const int MaxPageSize = ReportsSharedLimits.MaxPageSize;

    public const int DefaultPageSize = 50;

    public const int MaxExportRows = ReportsSharedLimits.MaxExportRows;
}
