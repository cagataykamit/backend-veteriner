namespace Backend.Veteriner.Application.Reports.Registers;

/// <summary>Defter raporu sabitleri; sayısal tavanlar <see cref="ReportsSharedLimits"/> ile hizalıdır.</summary>
public static class RegisterReportConstants
{
    /// <summary>Defterler yıllık okunur; Rapor Merkezi'nin 93 günlük sınırına tabi değildir.</summary>
    public const int MaxRangeDays = 366;

    public const int MaxPageSize = ReportsSharedLimits.MaxPageSize;

    public const int DefaultPageSize = 50;

    public const int MaxExportRows = ReportsSharedLimits.MaxExportRows;
}
