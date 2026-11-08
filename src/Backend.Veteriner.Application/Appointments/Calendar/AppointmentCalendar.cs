using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;

namespace Backend.Veteriner.Application.Appointments.Calendar;

/// <summary>
/// Takvime eklenecek randevu (TIER-020). Asgari veri kuralının tek yeri: başlık "{Klinik} randevusu", konum kliniğin adresi,
/// açıklama hayvan adı ve klinik telefonu. Teşhis, not, hekim ve sahip bilgisi girmez. Saatler UTC'dir.
/// </summary>
public sealed record AppointmentCalendarEvent(
    string Uid,
    DateTime StartUtc,
    DateTime EndUtc,
    string Summary,
    string? Location,
    string Description)
{
    private static readonly CompareInfo TurkishCompare = CultureInfo.GetCultureInfo("tr-TR").CompareInfo;

    public static AppointmentCalendarEvent Create(Guid appointmentId, DateTime startUtc, int durationMinutes, Clinic clinic, string? petName)
        => Create(appointmentId, startUtc, durationMinutes, clinic.Name, clinic.Address, clinic.District, clinic.City, clinic.Phone, petName);

    public static AppointmentCalendarEvent Create(
        Guid appointmentId,
        DateTime startUtc,
        int durationMinutes,
        string? clinicName,
        string? clinicAddress,
        string? clinicDistrict,
        string? clinicCity,
        string? clinicPhone,
        string? petName)
    {
        var start = startUtc.Kind switch
        {
            DateTimeKind.Utc => startUtc,
            DateTimeKind.Local => startUtc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(startUtc, DateTimeKind.Utc),
        };
        var minutes = Appointment.IsValidDurationMinutes(durationMinutes) ? durationMinutes : Appointment.DefaultDurationMinutes;
        var name = string.IsNullOrWhiteSpace(clinicName) ? "Klinik" : clinicName.Trim();

        var description = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(petName))
            description.Add($"Hayvan: {petName.Trim()}");
        if (!string.IsNullOrWhiteSpace(clinicPhone))
            description.Add($"Klinik telefonu: {clinicPhone.Trim()}");

        return new AppointmentCalendarEvent(
            AppointmentCalendar.UidFor(appointmentId),
            start,
            start.AddMinutes(minutes),
            $"{name} randevusu",
            JoinLocation(clinicAddress, clinicDistrict, clinicCity),
            string.Join("\n", description));
    }

    /// <summary>Adres; ilçe ve il adreste yazılı değilse sona eklenir. Hiçbiri yoksa konum yoktur.</summary>
    private static string? JoinLocation(string? address, string? district, string? city)
    {
        var text = address?.Trim() ?? string.Empty;
        foreach (var part in new[] { district?.Trim(), city?.Trim() })
        {
            if (string.IsNullOrEmpty(part) || TurkishCompare.IndexOf(text, part, CompareOptions.IgnoreCase) >= 0)
                continue;
            text = text.Length == 0 ? part : $"{text}, {part}";
        }

        return text.Length == 0 ? null : text;
    }
}

/// <summary>İndirilecek takvim dosyası.</summary>
public sealed record AppointmentCalendarFile(string FileName, string ContentType, byte[] Content);

/// <summary>
/// "Takvime ekle" çıktılarının tek yeri (TIER-020): RFC 5545 .ics dosyası, Google Takvim bağlantısı ve e-postadaki satırlar.
/// Saatler dosyada ve bağlantıda UTC'dir (<c>Z</c>); takvim uygulaması kendi saat dilimine çevirir. <c>TZID</c> + VTIMEZONE
/// kullanılmaz: Türkiye 2016'dan beri sabit UTC+3'tür ve elle yazılmış saat dilimi tanımı hata kaynağıdır.
/// </summary>
public static class AppointmentCalendar
{
    public const string IcsContentType = "text/calendar; charset=utf-8";
    public const string IcsFileName = "randevu.ics";
    public const string ProductId = "-//Vetinity//Randevu//TR";
    public const string UidDomain = "vetinity.com";
    public const string GoogleBaseUrl = "https://calendar.google.com/calendar/render";
    public const string DisplayTimeZone = "Europe/Istanbul";

    /// <summary>Hatırlatma alarmı: randevudan 1 saat önce.</summary>
    public const string AlarmTrigger = "-PT1H";

    /// <summary>RFC 5545 §3.1: satır sonu hariç en fazla 75 oktet; devam satırı tek boşlukla başlar.</summary>
    private const int MaxLineOctets = 75;

    /// <summary>
    /// Randevuya bağlı, sabit UID: randevu kimliğinin SHA-256 özetinden türer, kimliği açık etmez. Aynı randevunun hatırlatma ve
    /// onay e-postasındaki dosyaları takvimde aynı etkinliği günceller.
    /// </summary>
    public static string UidFor(Guid appointmentId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"vetinity-appointment:{appointmentId:D}"));
        return $"{Convert.ToHexStringLower(hash.AsSpan(0, 16))}@{UidDomain}";
    }

    public static AppointmentCalendarFile File(AppointmentCalendarEvent calendarEvent, DateTime stampUtc)
        => new(IcsFileName, IcsContentType, Encoding.UTF8.GetBytes(ToIcs(calendarEvent, stampUtc)));

    /// <summary>Tek VEVENT'li takvim; satırlar CRLF ile biter, metin kaçışlı ve 75 oktette katlanmıştır.</summary>
    public static string ToIcs(AppointmentCalendarEvent calendarEvent, DateTime stampUtc)
    {
        var sb = new StringBuilder();
        void Line(string content) => AppendFolded(sb, content);

        Line("BEGIN:VCALENDAR");
        Line("VERSION:2.0");
        Line($"PRODID:{ProductId}");
        Line("CALSCALE:GREGORIAN");
        Line("METHOD:PUBLISH");
        Line("BEGIN:VEVENT");
        Line($"UID:{calendarEvent.Uid}");
        Line($"DTSTAMP:{FormatUtc(stampUtc)}");
        Line($"DTSTART:{FormatUtc(calendarEvent.StartUtc)}");
        Line($"DTEND:{FormatUtc(calendarEvent.EndUtc)}");
        Line($"SUMMARY:{EscapeText(calendarEvent.Summary)}");
        if (!string.IsNullOrEmpty(calendarEvent.Location))
            Line($"LOCATION:{EscapeText(calendarEvent.Location)}");
        if (!string.IsNullOrEmpty(calendarEvent.Description))
            Line($"DESCRIPTION:{EscapeText(calendarEvent.Description)}");
        Line("BEGIN:VALARM");
        Line("ACTION:DISPLAY");
        Line($"DESCRIPTION:{EscapeText(calendarEvent.Summary)}");
        Line($"TRIGGER:{AlarmTrigger}");
        Line("END:VALARM");
        Line("END:VEVENT");
        Line("END:VCALENDAR");
        return sb.ToString();
    }

    /// <summary>Google Takvim "etkinlik oluştur" bağlantısı; saatler UTC (<c>Z</c>), gösterim İstanbul saati.</summary>
    public static string GoogleUrl(AppointmentCalendarEvent calendarEvent)
    {
        var query = new StringBuilder("action=TEMPLATE")
            .Append("&text=").Append(Uri.EscapeDataString(calendarEvent.Summary))
            .Append("&dates=").Append(FormatUtc(calendarEvent.StartUtc)).Append('/').Append(FormatUtc(calendarEvent.EndUtc));
        if (!string.IsNullOrEmpty(calendarEvent.Description))
            query.Append("&details=").Append(Uri.EscapeDataString(calendarEvent.Description));
        if (!string.IsNullOrEmpty(calendarEvent.Location))
            query.Append("&location=").Append(Uri.EscapeDataString(calendarEvent.Location));
        query.Append("&ctz=").Append(Uri.EscapeDataString(DisplayTimeZone));
        return $"{GoogleBaseUrl}?{query}";
    }

    /// <summary>E-postadaki "takvime ekle" satırları; .ics adresi yoksa (API kökü ayarlanmamış) yalnız Google bağlantısı.</summary>
    public static string EmailLines(string googleUrl, string? icsUrl)
    {
        var text = $"Randevuyu takviminize eklemek için:\nGoogle Takvim: {googleUrl}";
        return string.IsNullOrEmpty(icsUrl) ? text : $"{text}\nDiğer takvimler (.ics dosyası): {icsUrl}";
    }

    /// <summary>RFC 5545 §3.3.11 TEXT kaçışı: ters bölü, noktalı virgül, virgül ve satır sonu; diğer kontrol karakterleri atılır.</summary>
    public static string EscapeText(string value)
    {
        var sb = new StringBuilder(value.Length);
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        foreach (var ch in normalized)
        {
            switch (ch)
            {
                case '\\': sb.Append(@"\\"); break;
                case ';': sb.Append(@"\;"); break;
                case ',': sb.Append(@"\,"); break;
                case '\n': sb.Append(@"\n"); break;
                case '\t': sb.Append(ch); break;
                default:
                    if (!char.IsControl(ch))
                        sb.Append(ch);
                    break;
            }
        }

        return sb.ToString();
    }

    private static string FormatUtc(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
    }

    /// <summary>Satırı UTF-8 oktetine göre katlar; çok baytlı karakter iki satıra bölünmez.</summary>
    private static void AppendFolded(StringBuilder sb, string line)
    {
        var octets = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (octets + size > MaxLineOctets)
            {
                sb.Append("\r\n ");
                octets = 1;
            }

            sb.Append(rune.ToString());
            octets += size;
        }

        sb.Append("\r\n");
    }
}
