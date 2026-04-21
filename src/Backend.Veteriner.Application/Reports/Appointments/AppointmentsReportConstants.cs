namespace Backend.Veteriner.Application.Reports.Appointments;

/// <summary>Randevu raporu tarih aralığı ve export tavanı (dashboard ile karıştırılmaz).</summary>
public static class AppointmentsReportConstants
{
    public const int MaxRangeDays = 93;

    public const int MaxPageSize = 200;

    /// <summary>CSV/XLSX export satır üst sınırı.</summary>
    public const int MaxExportRows = 50_000;
}
