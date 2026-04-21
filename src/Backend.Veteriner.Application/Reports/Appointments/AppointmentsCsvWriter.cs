using System.Globalization;
using System.Text;
using Backend.Veteriner.Application.Reports.Appointments.Contracts.Dtos;

namespace Backend.Veteriner.Application.Reports.Appointments;

internal static class AppointmentsCsvWriter
{
    private const char Delimiter = ';';

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly TimeZoneInfo IstanbulTimeZone = ResolveIstanbulTimeZone();

    /// <summary>
    /// UTF-8 BOM; ayırıcı <c>;</c>. Kullanıcıya yönelik kolonlar (teknik ID yok); tarih Europe/Istanbul.
    /// JSON rapor <see cref="AppointmentReportItemDto"/> ile aynı pipeline’dan beslenir.
    /// </summary>
    public static byte[] WriteReportUtf8Bom(IReadOnlyList<AppointmentReportItemDto> rows)
    {
        var sb = new StringBuilder();
        sb.Append('\uFEFF');
        sb.Append("Randevu Zamanı");
        sb.Append(Delimiter);
        sb.Append("Klinik");
        sb.Append(Delimiter);
        sb.Append("Müşteri");
        sb.Append(Delimiter);
        sb.Append("Hayvan");
        sb.Append(Delimiter);
        sb.Append("Durum");
        sb.Append(Delimiter);
        sb.AppendLine("Not");

        foreach (var r in rows)
            AppendLine(sb, r);

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static void AppendLine(StringBuilder sb, AppointmentReportItemDto r)
    {
        var utc = DateTime.SpecifyKind(r.ScheduledAtUtc, DateTimeKind.Utc);
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
        AppendField(sb, AppointmentStatusTurkishDisplay.ToLabel(r.Status));
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
