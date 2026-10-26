using System.Globalization;
using System.Text;
using Backend.Veteriner.Application.Common.Csv;
using Backend.Veteriner.Application.Reports.OutstandingBalances.Contracts.Dtos;
using Backend.Veteriner.Domain.Charges;

namespace Backend.Veteriner.Application.Reports.OutstandingBalances;

/// <summary>
/// Borç raporu CSV’si: Türkiye Excel için noktalı virgül ayırıcı, UTF-8 BOM,
/// tarih Europe/Istanbul, teknik ID kolonları yok.
/// </summary>
internal static class OutstandingBalancesCsvWriter
{
    private const char Delimiter = ';';

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static byte[] WriteUtf8Bom(IReadOnlyList<OutstandingBalanceItemDto> rows)
    {
        var sb = new StringBuilder();
        sb.Append('﻿');
        sb.AppendCsvLine(string.Join(Delimiter, OutstandingBalancesReportColumns.Headers));

        foreach (var r in rows)
            AppendLine(sb, r);

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static void AppendLine(StringBuilder sb, OutstandingBalanceItemDto r)
    {
        AppendField(sb, r.ClientName);
        sb.Append(Delimiter);
        AppendField(sb, r.Phone ?? string.Empty);
        sb.Append(Delimiter);
        AppendField(sb, r.OutstandingAmount.ToString("0.00", Turkish));
        sb.Append(Delimiter);
        AppendField(sb, Charge.DefaultCurrency);
        sb.Append(Delimiter);
        AppendField(sb, r.OpenChargeCount.ToString(CultureInfo.InvariantCulture));
        sb.Append(Delimiter);
        AppendField(sb, OutstandingBalancesReportTime.ToIstanbul(r.OldestOpenChargeAtUtc).ToString("dd.MM.yyyy", Turkish));
        sb.AppendCsvLine();
    }

    private static void AppendField(StringBuilder sb, string value)
    {
        if (value.Length == 0)
            return;

        var needsQuote = value.Contains(Delimiter) || value.Contains('"') || value.Contains('\r') || value.Contains('\n');
        if (needsQuote)
        {
            sb.Append('"');
            sb.Append(value.Replace("\"", "\"\"", StringComparison.Ordinal));
            sb.Append('"');
        }
        else
        {
            sb.Append(value);
        }
    }
}
