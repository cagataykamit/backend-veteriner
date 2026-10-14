using System.Text.Encodings.Web;
using System.Text.Json;

namespace Backend.Veteriner.Application.Imports.Reading;

/// <summary>Hazırlık satırı kaynağı (<c>SourceJson</c>): başlık → metin (boş hücre null), dosya sırasıyla.</summary>
public static class ImportSourceRowJson
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        // Türkçe karakterler \uXXXX'e kaçırılmaz; depolama boyutu ve okunabilirlik için.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize(IReadOnlyList<string> headers, IReadOnlyList<string?> cells)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = i < cells.Count ? cells[i] : null;
                if (cell is null)
                    writer.WriteNull(headers[i]);
                else
                    writer.WriteString(headers[i], cell);
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }
}
