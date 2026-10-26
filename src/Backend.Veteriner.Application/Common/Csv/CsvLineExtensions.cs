using System.Text;

namespace Backend.Veteriner.Application.Common.Csv;

/// <summary>
/// CSV satır sonu her ortamda CRLF'dir (RFC 4180). <see cref="StringBuilder.AppendLine()"/> işletim sisteminin satır sonunu
/// yazar (Linux'ta LF); CSV yazıcıları onun yerine bu uzantıyı kullanır, böylece dosya sunucunun işletim sistemine bağlı olmaz.
/// </summary>
internal static class CsvLineExtensions
{
    public const string LineEnding = "\r\n";

    public static StringBuilder AppendCsvLine(this StringBuilder sb) => sb.Append(LineEnding);

    public static StringBuilder AppendCsvLine(this StringBuilder sb, string value) => sb.Append(value).Append(LineEnding);
}
