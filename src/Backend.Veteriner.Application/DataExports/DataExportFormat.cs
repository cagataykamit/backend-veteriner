using System.Globalization;
using System.Text;
using Backend.Veteriner.Application.Common.Csv;
using Backend.Veteriner.Application.Common.Time;

namespace Backend.Veteriner.Application.DataExports;

public enum DataExportColumnKind
{
    Id,
    Text,
    Number,
    Boolean,
    Date,
    Instant,
    Code,
}

/// <summary>
/// Paket dosyasının sütun tanımı: başlık, BENIOKU açıklaması ve değer tek yerde. Tür fabrika yöntemleriyle sabitlenir; zaman
/// sütunu dosyada iki sütundur (<c>{Ad}Utc</c>, <c>{Ad}Istanbul</c>).
/// </summary>
public sealed record DataExportColumn<TRow>(string Name, string Description, DataExportColumnKind Kind, Func<TRow, object?> Value)
{
    public static DataExportColumn<TRow> Id(string name, string description, Func<TRow, Guid?> value)
        => new(name, description, DataExportColumnKind.Id, row => value(row));

    public static DataExportColumn<TRow> Text(string name, string description, Func<TRow, string?> value)
        => new(name, description, DataExportColumnKind.Text, row => value(row));

    public static DataExportColumn<TRow> Number(string name, string description, Func<TRow, decimal?> value)
        => new(name, description, DataExportColumnKind.Number, row => value(row));

    public static DataExportColumn<TRow> Boolean(string name, string description, Func<TRow, bool?> value)
        => new(name, description, DataExportColumnKind.Boolean, row => value(row));

    /// <summary>Saatsiz takvim günü (doğum tarihi, SKT): saat dilimi dönüşümü yapılmaz.</summary>
    public static DataExportColumn<TRow> Date(string name, string description, Func<TRow, DateOnly?> value)
        => new(name, description, DataExportColumnKind.Date, row => value(row));

    /// <summary>UTC an: dosyada ISO 8601 UTC ve İstanbul saati olarak iki sütun.</summary>
    public static DataExportColumn<TRow> Instant(string name, string description, Func<TRow, DateTime?> value)
        => new(name, description, DataExportColumnKind.Instant, row => value(row));

    /// <summary>Sabit kod (durum, tür): enum adı; olası değerler açıklamaya eklenir.</summary>
    public static DataExportColumn<TRow> Code<TEnum>(string name, string description, Func<TRow, TEnum?> value)
        where TEnum : struct, Enum
        => new(name, $"{description} Değerler: {string.Join(", ", Enum.GetNames<TEnum>())}.", DataExportColumnKind.Code,
            row => value(row) is { } code ? code.ToString() : null);

    /// <inheritdoc cref="Code{TEnum}(string, string, Func{TRow, TEnum?})"/>
    public static DataExportColumn<TRow> Code<TEnum>(string name, string description, Func<TRow, TEnum> value)
        where TEnum : struct, Enum
        => Code<TEnum>(name, description, row => (TEnum?)value(row));

    public IReadOnlyList<string> Headers => Kind == DataExportColumnKind.Instant ? [$"{Name}Utc", $"{Name}Istanbul"] : [Name];
}

/// <summary>BENIOKU dosya bölümü: dosya adı, açıklama, satır sayısı ve sütun açıklamaları.</summary>
public sealed record DataExportFileSummary(string FileName, string Description, long RowCount, IReadOnlyList<(string Header, string Description)> Columns);

/// <summary>
/// Paket biçimi (DATA_EXPORT_API_CONTRACT.md §6) — tek yer. CSV: UTF-8 BOM, <c>;</c> ayırıcı, CRLF, ilk satır başlık; hücre
/// değiştirilmez (yalnız ayırıcı, tırnak ve satır sonu içeren hücre tırnaklanır). Sayı ondalık noktalı, zaman ISO 8601.
/// </summary>
public static class DataExportFormat
{
    /// <summary>Biçim sürümü; sütun kaldırma ya da anlam değişikliğinde artar (sütun eklemek geriye uyumludur).</summary>
    public const int Version = 1;

    public const char Delimiter = ';';
    public const string LineEnding = CsvLineExtensions.LineEnding;
    public const string ReadmeFileName = "BENIOKU.txt";

    public static readonly Encoding Utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public static string HeaderLine<TRow>(IReadOnlyList<DataExportColumn<TRow>> columns)
        => Line(columns.SelectMany(c => c.Headers));

    public static string RowLine<TRow>(IReadOnlyList<DataExportColumn<TRow>> columns, TRow row)
        => Line(columns.SelectMany(c => Cells(c.Kind, c.Value(row))));

    public static IReadOnlyList<(string Header, string Description)> Describe<TRow>(IReadOnlyList<DataExportColumn<TRow>> columns)
    {
        var result = new List<(string Header, string Description)>();
        foreach (var c in columns)
        {
            if (c.Kind == DataExportColumnKind.Instant)
            {
                result.Add((c.Headers[0], $"{c.Description} (UTC)"));
                result.Add((c.Headers[1], $"{c.Description} (İstanbul saati)"));
            }
            else
            {
                result.Add((c.Headers[0], c.Description));
            }
        }

        return result;
    }

    private static string Line(IEnumerable<string> cells) => string.Join(Delimiter, cells.Select(Quote));

    public static IReadOnlyList<string> Cells(DataExportColumnKind kind, object? value)
    {
        if (kind == DataExportColumnKind.Instant)
        {
            return value is DateTime utc ? [IsoUtc(utc), IsoIstanbul(utc)] : [string.Empty, string.Empty];
        }

        return [value switch
        {
            null => string.Empty,
            Guid id => id.ToString("D"),
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            bool flag => flag ? "Evet" : "Hayır",
            DateOnly day => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            string text => text,
            var other => Convert.ToString(other, CultureInfo.InvariantCulture) ?? string.Empty,
        }];
    }

    public static string IsoUtc(DateTime utc)
        => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static string IsoIstanbul(DateTime utc)
    {
        var normalized = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        var local = OperationDayBounds.ToLocal(normalized);
        var offset = local - normalized;
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        return $"{local.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)}{sign}{offset.Duration():hh\\:mm}";
    }

    public static string Quote(string value)
    {
        if (value.Length == 0)
            return value;

        var needsQuote = value.Contains(Delimiter) || value.Contains('"') || value.Contains('\r') || value.Contains('\n');
        return needsQuote ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
    }

    /// <summary>Pakete eklenen açıklama dosyası: biçim kuralları, verilmeyenler, dosya ve sütun listesi.</summary>
    public static string Readme(DateTime generatedAtUtc, IReadOnlyList<DataExportFileSummary> files)
    {
        var sb = new StringBuilder();
        sb.AppendCsvLine("Vetinity — tüm veri dışa aktarma paketi");
        sb.AppendCsvLine($"Biçim sürümü: {Version}");
        sb.AppendCsvLine($"Oluşturma zamanı (UTC): {IsoUtc(generatedAtUtc)}");
        sb.AppendCsvLine($"Oluşturma zamanı (İstanbul): {IsoIstanbul(generatedAtUtc)}");
        sb.AppendCsvLine();
        sb.AppendCsvLine("Genel kurallar");
        sb.AppendCsvLine("- Her .csv dosyası UTF-8 (BOM'lu), ayırıcı noktalı virgül (;), satır sonu CRLF; ilk satır başlıktır.");
        sb.AppendCsvLine("- Her dosyanın ilk sütunu Id: kaydın kalıcı kimliği (GUID). Diğer dosyalardaki ...Id sütunları bu kimliğe bağlanır.");
        sb.AppendCsvLine("- Zaman sütunları iki kez verilir: ...Utc (ISO 8601, UTC, Z ile) ve ...Istanbul (ISO 8601, İstanbul saati, +03:00 gibi).");
        sb.AppendCsvLine("- Saatsiz tarihler yyyy-MM-dd biçimindedir.");
        sb.AppendCsvLine("- Sayı ve tutarlar ondalık noktayla yazılır (1250.50); binlik ayırıcı yoktur. Para birimi ayrı sütundadır.");
        sb.AppendCsvLine("- Evet/Hayır sütunları \"Evet\" ya da \"Hayır\" değerini alır; boş hücre değer olmadığını gösterir.");
        sb.AppendCsvLine("- Kod sütunları (durum, tür) sabit İngilizce adlardır; olası değerler sütun açıklamasındadır.");
        sb.AppendCsvLine("- Hücreler değiştirilmeden verilir. \"=\", \"+\", \"-\" ya da \"@\" ile başlayan metin Excel'de formül sanılabilir; dosyayı içe aktarma sihirbazıyla, sütunları metin olarak açın.");
        sb.AppendCsvLine("- Silinmiş ya da iptal edilmiş kayıtlar da verilir; durum ve iptal sütunlarına bakın.");
        sb.AppendCsvLine();
        sb.AppendCsvLine("Pakette olmayanlar");
        sb.AppendCsvLine("- Parola özetleri, oturum ve yenileme belirteçleri, doğrulama kodları, API anahtarı özetleri.");
        sb.AppendCsvLine("- Hasta sahibi portalı hesap kimlik bilgileri (yalnız bağ durumu verilir).");
        sb.AppendCsvLine("- Gönderilen e-posta ve bildirim içerikleri (hatırlatma günlüğünde yalnız ne zaman ve hangi kayıt için gönderildiği vardır).");
        sb.AppendCsvLine("- Yüklenen dosyaların içeriği (görüntü, lab ekleri); dosyalar.csv yalnız kayıt ve dosya adını verir.");
        sb.AppendCsvLine("- Denetim kayıtları, iç teknik alanlar ve başka kurumların verisi.");
        sb.AppendCsvLine();
        sb.AppendCsvLine("Dosyalar");
        foreach (var file in files)
            sb.AppendCsvLine($"- {file.FileName}: {file.Description} ({file.RowCount} kayıt)");

        foreach (var file in files)
        {
            sb.AppendCsvLine();
            sb.AppendCsvLine($"{file.FileName} — {file.Description}");
            foreach (var (header, description) in file.Columns)
                sb.AppendCsvLine($"  {header}: {description}");
        }

        return sb.ToString();
    }
}
