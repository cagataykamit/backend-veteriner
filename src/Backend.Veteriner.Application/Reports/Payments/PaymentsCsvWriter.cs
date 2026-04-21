using System.Globalization;
using System.Text;
using Backend.Veteriner.Application.Reports.Payments.Contracts.Dtos;

namespace Backend.Veteriner.Application.Reports.Payments;

/// <summary>
/// Klinik içi tahsilat raporu CSV’si: Türkiye Excel için noktalı virgül ayırıcı, UTF-8 BOM,
/// tarih/saat Europe/Istanbul, teknik ID kolonları yok.
/// </summary>
internal static class PaymentsCsvWriter
{
    private const char Delimiter = ';';

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly TimeZoneInfo IstanbulTimeZone = ResolveIstanbulTimeZone();

    /// <summary>UTF-8 BOM; kolonlar iş odaklı Türkçe başlıklar; ayırıcı <c>;</c> (Excel TR).</summary>
    public static byte[] WriteClinicReceiptReportUtf8Bom(IReadOnlyList<PaymentReportItemDto> rows)
    {
        var sb = new StringBuilder();
        sb.Append('\uFEFF');
        sb.Append("Tarih");
        sb.Append(Delimiter);
        sb.Append("Klinik");
        sb.Append(Delimiter);
        sb.Append("Müşteri");
        sb.Append(Delimiter);
        sb.Append("Hayvan");
        sb.Append(Delimiter);
        sb.Append("Tutar");
        sb.Append(Delimiter);
        sb.Append("Para Birimi");
        sb.Append(Delimiter);
        sb.Append("Ödeme Yöntemi");
        sb.Append(Delimiter);
        sb.AppendLine("Not");

        foreach (var r in rows)
            AppendLine(sb, r);

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static void AppendLine(StringBuilder sb, PaymentReportItemDto r)
    {
        var paidUtc = DateTime.SpecifyKind(r.PaidAtUtc, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(paidUtc, IstanbulTimeZone);
        var dateText = local.ToString("dd.MM.yyyy HH:mm", Turkish);

        AppendField(sb, dateText);
        sb.Append(Delimiter);
        AppendField(sb, r.ClinicName);
        sb.Append(Delimiter);
        AppendField(sb, r.ClientName);
        sb.Append(Delimiter);
        AppendField(sb, r.PetName);
        sb.Append(Delimiter);
        AppendField(sb, r.Amount.ToString("0.##", Turkish));
        sb.Append(Delimiter);
        AppendField(sb, r.Currency);
        sb.Append(Delimiter);
        AppendField(sb, PaymentMethodTurkishDisplay.ToLabel(r.Method));
        sb.Append(Delimiter);
        AppendField(sb, r.Notes ?? string.Empty);
        sb.AppendLine();
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

    private static TimeZoneInfo ResolveIstanbulTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time");
        }
    }
}
