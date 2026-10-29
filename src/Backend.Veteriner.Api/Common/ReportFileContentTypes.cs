namespace Backend.Veteriner.Api.Common;

/// <summary><c>ReportsController</c> dosya yanıtları için Content-Type sabitleri (Faz 6D).</summary>
public static class ReportFileContentTypes
{
    public const string CsvUtf8 = "text/csv; charset=utf-8";

    public const string Xlsx =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public const string Pdf = "application/pdf";

    /// <summary>Dosya adının uzantısına göre içerik türü (defter uçları biçimi istekte taşır).</summary>
    public static string ForFileName(string fileName)
        => Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".xlsx" => Xlsx,
            ".pdf" => Pdf,
            _ => CsvUtf8,
        };
}
