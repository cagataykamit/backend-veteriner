using System.Globalization;
using System.Text;
using Backend.Veteriner.Application.Reports.Examinations.Contracts.Dtos;

namespace Backend.Veteriner.Application.Reports.Examinations;

internal static class ExaminationsCsvWriter
{
    private const char Delimiter = ';';

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly TimeZoneInfo IstanbulTimeZone = ResolveIstanbulTimeZone();

    /// <summary>UTF-8 BOM; ayırıcı <c>;</c>. Kullanıcıya yönelik (teknik ID yok); tarih Europe/Istanbul.</summary>
    public static byte[] WriteReportUtf8Bom(IReadOnlyList<ExaminationReportItemDto> rows)
    {
        var sb = new StringBuilder();
        sb.Append('\uFEFF');
        sb.Append("Muayene Zamanı");
        sb.Append(Delimiter);
        sb.Append("Klinik");
        sb.Append(Delimiter);
        sb.Append("Müşteri");
        sb.Append(Delimiter);
        sb.Append("Hayvan");
        sb.Append(Delimiter);
        sb.Append("Bağlı Randevu");
        sb.Append(Delimiter);
        sb.Append("Geliş Nedeni");
        sb.Append(Delimiter);
        sb.Append("Bulgular");
        sb.Append(Delimiter);
        sb.Append("Değerlendirme");
        sb.Append(Delimiter);
        sb.AppendLine("Not");

        foreach (var r in rows)
            AppendLine(sb, r);

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static void AppendLine(StringBuilder sb, ExaminationReportItemDto r)
    {
        var utc = DateTime.SpecifyKind(r.ExaminedAtUtc, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, IstanbulTimeZone);
        var dateText = local.ToString("dd.MM.yyyy HH:mm", Turkish);

        AppendField(sb, dateText);
        sb.Append(Delimiter);
        AppendField(sb, r.ClinicName);
        sb.Append(Delimiter);
        AppendField(sb, r.ClientName);
        sb.Append(Delimiter);
        AppendField(sb, r.PetName);
        sb.Append(Delimiter);
        AppendField(sb, r.AppointmentId.HasValue ? "Var" : "Yok");
        sb.Append(Delimiter);
        AppendField(sb, r.VisitReason);
        sb.Append(Delimiter);
        AppendField(sb, r.Findings);
        sb.Append(Delimiter);
        AppendField(sb, r.Assessment ?? string.Empty);
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
