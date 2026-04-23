using Backend.Veteriner.Application.Reports;

namespace Backend.Veteriner.Application.Reports.Examinations;

/// <summary>Muayene raporu sabitleri; sayısal tavanlar <see cref="ReportsSharedLimits"/> ile hizalıdır.</summary>
public static class ExaminationsReportConstants
{
    public const int MaxRangeDays = ReportsSharedLimits.MaxRangeDays;

    public const int MaxPageSize = ReportsSharedLimits.MaxPageSize;

    public const int MaxExportRows = ReportsSharedLimits.MaxExportRows;
}
