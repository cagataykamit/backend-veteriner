using System.Text;
using Backend.Veteriner.Application.Appointments.Calendar;
using Backend.Veteriner.Domain.Appointments;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Appointments;

/// <summary>TIER-020: .ics (RFC 5545 kaçış, katlama, UTF-8, UID, saat), Google Takvim bağlantısı ve asgari veri.</summary>
public sealed class AppointmentCalendarTests
{
    private static readonly Guid AppointmentId = Guid.Parse("6b0f6c1e-2f43-4a51-9d55-1f5e2f6b7a10");

    // İstanbul 10:00 = 07:00 UTC.
    private static readonly DateTime StartUtc = new(2026, 11, 9, 7, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime StampUtc = new(2026, 11, 8, 6, 15, 30, DateTimeKind.Utc);

    private static AppointmentCalendarEvent Event(
        string clinicName = "Merkez Veteriner",
        string? address = "Bağdat Cad. No:5",
        string? district = "Kadıköy",
        string? city = "İstanbul",
        string? phone = "0216 000 00 00",
        string? petName = "Pamuk",
        int duration = 45)
        => AppointmentCalendarEvent.Create(AppointmentId, StartUtc, duration, clinicName, address, district, city, phone, petName);

    private static string[] Unfold(string ics)
        => ics.Replace("\r\n ", string.Empty, StringComparison.Ordinal).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void Event_Should_Carry_Minimal_Data_Only()
    {
        var e = Event();

        e.Summary.Should().Be("Merkez Veteriner randevusu");
        e.Location.Should().Be("Bağdat Cad. No:5, Kadıköy, İstanbul");
        e.Description.Should().Be("Hayvan: Pamuk\nKlinik telefonu: 0216 000 00 00");
        e.StartUtc.Should().Be(StartUtc);
        e.EndUtc.Should().Be(StartUtc.AddMinutes(45));
        typeof(AppointmentCalendarEvent).GetProperties().Select(p => p.Name)
            .Should().BeEquivalentTo("Uid", "StartUtc", "EndUtc", "Summary", "Location", "Description");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    public void Event_Should_Fall_Back_To_Default_Duration(int invalidDuration)
        => Event(duration: invalidDuration).EndUtc.Should().Be(StartUtc.AddMinutes(Appointment.DefaultDurationMinutes));

    [Fact]
    public void Event_Should_Skip_Empty_Parts_And_Not_Repeat_City_In_Address()
    {
        var e = Event(address: "Moda Cad. 1, Kadıköy / İSTANBUL", phone: null, petName: " ");

        e.Location.Should().Be("Moda Cad. 1, Kadıköy / İSTANBUL", "ilçe ve il adreste yazılı");
        e.Description.Should().BeEmpty();
        Event(address: null, district: null, city: null).Location.Should().BeNull();
    }

    [Fact]
    public void Uid_Should_Be_Stable_Per_Appointment_And_Not_Reveal_Id()
    {
        var uid = AppointmentCalendar.UidFor(AppointmentId);

        uid.Should().Be(AppointmentCalendar.UidFor(AppointmentId)).And.EndWith("@vetinity.com").And.HaveLength(32 + "@vetinity.com".Length);
        uid.Should().NotContain(AppointmentId.ToString("N")).And.NotContain(AppointmentId.ToString("D"));
        AppointmentCalendar.UidFor(Guid.NewGuid()).Should().NotBe(uid);
        Event().Uid.Should().Be(uid);
        Event(clinicName: "Başka", duration: 30).Uid.Should().Be(uid, "saat/ayrıntı değişse de aynı etkinlik");
    }

    [Fact]
    public void Ics_Should_Have_Single_Event_With_Utc_Times_Alarm_And_Crlf()
    {
        var ics = AppointmentCalendar.ToIcs(Event(), StampUtc);
        var lines = Unfold(ics);

        ics.Should().EndWith("\r\n");
        ics.Replace("\r\n", string.Empty, StringComparison.Ordinal).Should().NotContain("\n");
        lines.First().Should().Be("BEGIN:VCALENDAR");
        lines.Last().Should().Be("END:VCALENDAR");
        lines.Should().Contain(["VERSION:2.0", "PRODID:-//Vetinity//Randevu//TR", "METHOD:PUBLISH"]);
        lines.Count(l => l == "BEGIN:VEVENT").Should().Be(1);
        lines.Should().Contain($"UID:{AppointmentCalendar.UidFor(AppointmentId)}");
        lines.Should().Contain("DTSTAMP:20261108T061530Z");
        lines.Should().Contain("DTSTART:20261109T070000Z", "UTC, Z ile");
        lines.Should().Contain("DTEND:20261109T074500Z");
        lines.Should().NotContain(l => l.Contains("TZID") || l.StartsWith("BEGIN:VTIMEZONE"));
        lines.Should().Contain(["BEGIN:VALARM", "ACTION:DISPLAY", "TRIGGER:-PT1H", "END:VALARM"]);
    }

    [Fact]
    public void Ics_Text_Should_Escape_Backslash_Semicolon_Comma_And_Newline()
    {
        AppointmentCalendar.EscapeText("a\\b;c,d\ne\r\nf\rg\u0007h\ti").Should().Be(@"a\\b\;c\,d\ne\nf\ng" + "h\ti");

        var lines = Unfold(AppointmentCalendar.ToIcs(Event(clinicName: "Dr. A; B, C", address: "Sk.\\1"), StampUtc));

        lines.Should().Contain(@"SUMMARY:Dr. A\; B\, C randevusu");
        lines.Should().Contain(@"LOCATION:Sk.\\1\, Kadıköy\, İstanbul");
        lines.Should().Contain(@"DESCRIPTION:Hayvan: Pamuk\nKlinik telefonu: 0216 000 00 00");
    }

    [Fact]
    public void Ics_Should_Fold_At_75_Octets_Without_Splitting_Turkish_Characters()
    {
        var longName = string.Concat(Enumerable.Repeat("Çağrışım Şöleni Ğİİ ", 8));
        var ics = AppointmentCalendar.ToIcs(Event(clinicName: longName), StampUtc);

        foreach (var physical in ics.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            Encoding.UTF8.GetByteCount(physical).Should().BeLessThanOrEqualTo(75, physical);
            physical.Should().NotContain("�");
        }

        ics.Should().Contain("\r\n ", "uzun satır katlanır");
        Unfold(ics).Should().Contain($"SUMMARY:{longName.Trim()} randevusu", "katlama geri açılınca metin bozulmaz");

        var file = AppointmentCalendar.File(Event(clinicName: longName), StampUtc);
        file.ContentType.Should().Be("text/calendar; charset=utf-8");
        file.FileName.Should().EndWith(".ics");
        file.Content.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF }, "BOM yok");
        Encoding.UTF8.GetString(file.Content).Should().Contain("Çağrışım").And.Be(AppointmentCalendar.ToIcs(Event(clinicName: longName), StampUtc));
    }

    [Fact]
    public void Ics_Should_Treat_Unspecified_Database_Time_As_Utc()
    {
        var fromDb = DateTime.SpecifyKind(StartUtc, DateTimeKind.Unspecified);
        var e = AppointmentCalendarEvent.Create(AppointmentId, fromDb, 30, "Merkez", null, null, null, null, "Pamuk");

        Unfold(AppointmentCalendar.ToIcs(e, StampUtc)).Should().Contain("DTSTART:20261109T070000Z");
    }

    [Fact]
    public void Google_Url_Should_Have_Template_Utc_Dates_And_Escaped_Fields()
    {
        var url = AppointmentCalendar.GoogleUrl(Event());

        url.Should().StartWith("https://calendar.google.com/calendar/render?action=TEMPLATE&");
        var query = url[(url.IndexOf('?') + 1)..].Split('&')
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));

        query["action"].Should().Be("TEMPLATE");
        query["text"].Should().Be("Merkez Veteriner randevusu");
        query["dates"].Should().Be("20261109T070000Z/20261109T074500Z");
        query["location"].Should().Be("Bağdat Cad. No:5, Kadıköy, İstanbul");
        query["details"].Should().Be("Hayvan: Pamuk\nKlinik telefonu: 0216 000 00 00");
        query["ctz"].Should().Be("Europe/Istanbul");
        query.Keys.Should().BeEquivalentTo("action", "text", "dates", "location", "details", "ctz");
        url.Should().NotContain(" ").And.NotContain("ğ");
    }

    [Fact]
    public void Email_Lines_Should_List_Google_And_Ics_Links()
    {
        AppointmentCalendar.EmailLines("https://g.test/x", "https://api.test/a.ics?token=t")
            .Should().Be("Randevuyu takviminize eklemek için:\nGoogle Takvim: https://g.test/x\nDiğer takvimler (.ics dosyası): https://api.test/a.ics?token=t");
        AppointmentCalendar.EmailLines("https://g.test/x", null).Should().NotContain(".ics");
    }
}
