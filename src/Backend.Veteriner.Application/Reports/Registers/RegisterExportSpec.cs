namespace Backend.Veteriner.Application.Reports.Registers;

/// <summary>Rapor dışa aktarma biçimi (defterler, gün sonu ve klasik raporlar ortak kullanır).</summary>
public enum RegisterExportFormat
{
    Csv,
    Xlsx,
    Pdf
}

/// <summary>
/// Bir defterin dışa aktarma tanımı: sütunlar, XLSX sayfa adı, dosya adı öneki ve PDF başlığı (tek yerde, sütun listesiyle birlikte).
/// </summary>
public sealed record RegisterExportSpec<TRow>(
    IReadOnlyList<RegisterColumn<TRow>> Columns,
    string SheetName,
    string FileBaseName,
    string Title);
