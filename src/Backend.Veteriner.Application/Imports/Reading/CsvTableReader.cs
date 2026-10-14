using System.Text;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Imports.Reading;

/// <summary>
/// CSV okuyucu: kodlama algılama (BOM → katı UTF-8 → Windows-1254), ayırıcı algılama (<c>; , sekme</c>) ve
/// RFC 4180 ayrıştırma. Değerler olduğu gibi okunur; hiçbir hücre yorumlanmaz.
/// </summary>
public static class CsvTableReader
{
    private const int DelimiterProbeRecords = 20;
    private const int DelimiterProbeChars = 200_000;
    private static readonly char[] DelimiterCandidates = [';', ',', '\t'];

    public static bool TryParseDelimiter(string? value, out char? delimiter)
    {
        delimiter = null;
        if (string.IsNullOrEmpty(value))
            return true;

        switch (value.ToLowerInvariant())
        {
            case ";" or "semicolon": delimiter = ';'; return true;
            case "," or "comma": delimiter = ','; return true;
            case "\t" or "\\t" or "tab": delimiter = '\t'; return true;
            default: return false;
        }
    }

    /// <summary><paramref name="utf8"/>: true UTF-8, false Windows-1254, null otomatik.</summary>
    public static bool TryParseEncoding(string? value, out bool? utf8)
    {
        utf8 = null;
        if (string.IsNullOrWhiteSpace(value))
            return true;

        switch (value.Trim().ToLowerInvariant())
        {
            case "utf-8" or "utf8": utf8 = true; return true;
            case "windows-1254" or "cp1254" or "1254" or "iso-8859-9": utf8 = false; return true;
            default: return false;
        }
    }

    public static Result<ImportSourceTable?> Read(
        ImportTable table,
        byte[] content,
        string? encoding,
        string? delimiter,
        ImportFileLimits limits,
        List<ImportReadWarning> warnings)
    {
        TryParseEncoding(encoding, out var forcedUtf8);
        TryParseDelimiter(delimiter, out var forcedDelimiter);

        var text = Decode(content, forcedUtf8);
        if (text.Contains('�'))
        {
            warnings.Add(new ImportReadWarning(
                "Imports.EncodingSuspect",
                table,
                $"{Mapping.ImportCanonicalNames.Display(table)} dosyasında okunamayan karakterler var; kodlama (UTF-8 / Windows-1254) yanlış olabilir."));
        }

        var separator = forcedDelimiter ?? DetectDelimiter(text);
        return ImportTableAssembler.Assemble(table, Records(text, separator), limits);
    }

    /// <summary>BOM → (zorlanmışsa) seçilen kodlama → katı UTF-8 → Windows-1254.</summary>
    internal static string Decode(byte[] content, bool? forcedUtf8)
    {
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
            return Encoding.UTF8.GetString(content, 3, content.Length - 3);
        if (content.Length >= 2 && content[0] == 0xFF && content[1] == 0xFE)
            return Encoding.Unicode.GetString(content, 2, content.Length - 2);
        if (content.Length >= 2 && content[0] == 0xFE && content[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(content, 2, content.Length - 2);

        if (forcedUtf8 == true)
            return Encoding.UTF8.GetString(content);
        if (forcedUtf8 == false)
            return Windows1254().GetString(content);

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(content);
        }
        catch (DecoderFallbackException)
        {
            return Windows1254().GetString(content);
        }
    }

    private static Encoding Windows1254()
    {
        // .NET Core'da kod sayfaları sağlayıcı kaydı ister; tekrar kayıt zararsızdır.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1254);
    }

    /// <summary>
    /// İlk kayıtlarda başlıkla aynı alan sayısına sahip kayıt sayısı en yüksek aday seçilir;
    /// eşitlikte <c>; , sekme</c> sırası. Hiçbiri çok alanlı değilse <c>;</c>.
    /// </summary>
    internal static char DetectDelimiter(string text)
    {
        var truncated = text.Length > DelimiterProbeChars;
        var probe = truncated ? text[..DelimiterProbeChars] : text;

        var best = ';';
        var bestScore = 0;
        foreach (var candidate in DelimiterCandidates)
        {
            var records = Records(probe, candidate)
                .Where(r => !r.IsBlank)
                .Take(DelimiterProbeRecords + 1)
                .ToList();
            if (truncated && records.Count > DelimiterProbeRecords)
                records.RemoveAt(records.Count - 1);
            if (records.Count == 0)
                continue;

            var headerCount = records[0].Cells.Count;
            if (headerCount < 2)
                continue;

            var score = records.Take(DelimiterProbeRecords).Count(r => r.Cells.Count == headerCount);
            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>Kayıt numarası başlık dahil 1'den başlar; boş kayıtlar da sayılır.</summary>
    internal static IEnumerable<ImportRawRow> Records(string text, char delimiter)
    {
        var field = new StringBuilder();
        var cells = new List<string?>();
        var recordNumber = 1;
        var inQuotes = false;
        var fieldWasQuoted = false;
        var i = text.Length > 0 && text[0] == '﻿' ? 1 : 0;

        while (i < text.Length)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 2;
                        continue;
                    }

                    inQuotes = false;
                    i++;
                    continue;
                }

                field.Append(c);
                i++;
            }
            else if (c == '"' && field.Length == 0 && !fieldWasQuoted)
            {
                inQuotes = true;
                fieldWasQuoted = true;
                i++;
            }
            else if (c == delimiter)
            {
                cells.Add(Finish(field));
                fieldWasQuoted = false;
                i++;
            }
            else if (c is '\r' or '\n')
            {
                cells.Add(Finish(field));
                fieldWasQuoted = false;
                yield return new ImportRawRow(recordNumber++, cells);
                cells = [];
                i += c == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
            }
            else
            {
                field.Append(c);
                i++;
            }
        }

        // Dosya satır sonuyla bitmiyorsa son kayıt.
        if (field.Length > 0 || cells.Count > 0 || fieldWasQuoted)
        {
            cells.Add(Finish(field));
            yield return new ImportRawRow(recordNumber, cells);
        }
    }

    private static string? Finish(StringBuilder field)
    {
        var value = field.ToString();
        field.Clear();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
