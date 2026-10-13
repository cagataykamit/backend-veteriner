using System.Globalization;
using System.Text;
using Backend.Veteriner.Application.Common.Time;

namespace Backend.Veteriner.Application.Reports.Registers;

/// <summary>
/// Defter CSV'si: Türkiye Excel için noktalı virgül ayırıcı, UTF-8 BOM, tarih Europe/Istanbul, teknik ID kolonu yok.
/// Başlıklar ve değerler yalnız <see cref="RegisterColumn{TRow}"/> listesinden gelir.
/// </summary>
internal static class RegisterCsvWriter
{
    private const char Delimiter = ';';

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static byte[] WriteUtf8Bom<TRow>(IReadOnlyList<RegisterColumn<TRow>> columns, IEnumerable<TRow> rows)
    {
        var sb = new StringBuilder();
        sb.Append('﻿');
        sb.AppendLine(string.Join(Delimiter, columns.Select(c => Quote(c.CsvHeader))));

        foreach (var row in rows)
        {
            for (var i = 0; i < columns.Count; i++)
            {
                if (i > 0)
                    sb.Append(Delimiter);
                sb.Append(Quote(Format(columns[i], row)));
            }

            sb.AppendLine();
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string Format<TRow>(RegisterColumn<TRow> column, TRow row)
        => column.Value(row) switch
        {
            null => string.Empty,
            DateTime utc => OperationDayBounds.ToLocalDate(utc).ToString("dd.MM.yyyy", Turkish),
            DateOnly day => day.ToString("dd.MM.yyyy", Turkish),
            decimal number => number.ToString("0.###", Turkish),
            var other => other.ToString() ?? string.Empty,
        };

    private static string Quote(string value)
    {
        if (value.Length == 0)
            return value;

        var needsQuote = value.Contains(Delimiter) || value.Contains('"') || value.Contains('\r') || value.Contains('\n');
        return needsQuote ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
    }
}
