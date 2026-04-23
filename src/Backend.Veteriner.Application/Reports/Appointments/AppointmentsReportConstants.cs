using Backend.Veteriner.Application.Reports;

namespace Backend.Veteriner.Application.Reports.Appointments;

/// <summary>Randevu raporu sabitleri; sayısal tavanlar <see cref="ReportsSharedLimits"/> ile hizalıdır.</summary>
public static class AppointmentsReportConstants
{
    public const int MaxRangeDays = ReportsSharedLimits.MaxRangeDays;

    public const int MaxPageSize = ReportsSharedLimits.MaxPageSize;

    public const int MaxExportRows = ReportsSharedLimits.MaxExportRows;
}
