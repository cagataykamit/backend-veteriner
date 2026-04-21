using System.Globalization;
using System.Text;
using Backend.Veteriner.Application.Reports.Vaccinations.Contracts.Dtos;

namespace Backend.Veteriner.Application.Reports.Vaccinations;

internal static class VaccinationsCsvWriter
{
    private const char Delimiter = ';';

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly TimeZoneInfo IstanbulTimeZone = ResolveIstanbulTimeZone();

    /// <summary>UTF-8 BOM; kullanıcı odaklı kolonlar (teknik ID yok); tarihler Europe/Istanbul metin.</summary>
    public static byte[] WriteReportUtf8Bom(IReadOnlyList<VaccinationReportItemDto> rows)
    {
        var sb = new StringBuilder();
        sb.Append('\uFEFF');
        sb.Append("Rapor Tarihi");
        sb.Append(Delimiter);
        sb.Append("Klinik");
        sb.Append(Delimiter);
        sb.Append("Müşteri");
        sb.Append(Delimiter);
        sb.Append("Hayvan");
        sb.Append(Delimiter);
        sb.Append("Aşı");
        sb.Append(Delimiter);
        sb.Append("Durum");
        sb.Append(Delimiter);
        sb.Append("Uygulama Tarihi");
        sb.Append(Delimiter);
        sb.Append("Sonraki Tarih");
        sb.Append(Delimiter);
        sb.AppendLine("Not");

        foreach (var r in rows)
            AppendLine(sb, r);

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static void AppendLine(StringBuilder sb, VaccinationReportItemDto r)
    {
        AppendField(sb, FormatOptionalLocal(r.EffectiveReportDateUtc));
        sb.Append(Delimiter);
        AppendField(sb, r.ClinicName);
        sb.Append(Delimiter);
        AppendField(sb, r.ClientName);
        sb.Append(Delimiter);
        AppendField(sb, r.PetName);
        sb.Append(Delimiter);
        AppendField(sb, r.VaccineName);
        sb.Append(Delimiter);
        AppendField(sb, VaccinationStatusTurkishDisplay.ToLabel(r.Status));
        sb.Append(Delimiter);
        AppendField(sb, FormatOptionalLocal(r.AppliedAtUtc));
        sb.Append(Delimiter);
        AppendField(sb, FormatOptionalLocal(r.NextDueAtUtc));
        sb.Append(Delimiter);
        AppendField(sb, r.Notes ?? string.Empty);
        sb.AppendLine();
    }

    private static string FormatOptionalLocal(DateTime? utc)
    {
        if (utc is null)
            return string.Empty;
        var u = DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(u, IstanbulTimeZone);
        return local.ToString("dd.MM.yyyy HH:mm", Turkish);
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
