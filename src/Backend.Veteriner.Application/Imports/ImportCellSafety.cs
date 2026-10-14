namespace Backend.Veteriner.Application.Imports;

/// <summary>
/// Dışarı yazılan dosyalarda (şablon, hata raporu) formül/makro enjeksiyonu koruması: <c>= + - @</c> (ve sekme/CR)
/// ile başlayan metin hücresinin başına <c>'</c> eklenir. Dosyadan okunan değerler bu yolla değiştirilmez;
/// okunan metin olduğu gibi saklanır ve hiçbir zaman formül olarak yorumlanmaz.
/// </summary>
public static class ImportCellSafety
{
    public static string ForExport(string value)
        => value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
}
