namespace Backend.Veteriner.Application.Reports.Registers;

public enum RegisterExportFormat
{
    Csv,
    Xlsx
}

/// <summary>Bir defterin dışa aktarma tanımı: sütunlar, XLSX sayfa adı ve dosya adı öneki (tek yerde, sütun listesiyle birlikte).</summary>
public sealed record RegisterExportSpec<TRow>(
    IReadOnlyList<RegisterColumn<TRow>> Columns,
    string SheetName,
    string FileBaseName);
