namespace Backend.Veteriner.Application.Reports.Vaccinations;

/// <summary>Aşı raporu tarih aralığı ve export tavanı (dashboard ile karıştırılmaz).</summary>
public static class VaccinationsReportConstants
{
    public const int MaxRangeDays = 93;

    public const int MaxPageSize = 200;

    public const int MaxExportRows = 50_000;
}
