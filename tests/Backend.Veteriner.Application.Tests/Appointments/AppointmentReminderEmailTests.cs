using Backend.Veteriner.Application.Appointments.Calendar;
using Backend.Veteriner.Application.Appointments.OwnerLinks;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Domain.Appointments;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Appointments;

/// <summary>TIER-019/020: hatırlatma e-postasındaki bağlantılar, iptal kapalıyken "kliniği arayın" satırı ve takvim satırları.</summary>
public sealed class AppointmentReminderEmailTests
{
    private static readonly IssuedAppointmentOwnerLink Links = new(
        AppointmentOwnerLink.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "hash", DateTime.UtcNow.AddDays(1), DateTime.UtcNow),
        "https://portal.test/randevu-yanit?token=t&islem=onayla",
        "https://portal.test/randevu-yanit?token=t&islem=iptal");

    private static readonly string CalendarLines = AppointmentCalendar.EmailLines(
        "https://calendar.google.com/calendar/render?action=TEMPLATE&text=x",
        "https://api.test/api/portal/v1/appointment-link/calendar.ics?token=c");

    [Fact]
    public void Body_Should_Contain_Both_Links_When_Cancel_Enabled()
    {
        var body = AppointmentReminderEmail.Body("Ali Veli", "Pamuk", "Merkez", "09.11.2026 10:00", Links, cancelLinkEnabled: true, "02120000000", CalendarLines);

        body.Should().StartWith("Sayın Ali Veli,\nPamuk için Merkez kliniğinde 09.11.2026 10:00 tarihinde planlanan randevunuzu hatırlatırız.");
        body.Should().Contain(Links.ConfirmUrl).And.Contain(Links.CancelUrl).And.Contain("yalnız bir kez");
        body.Should().NotContain("kliniği arayın").And.NotContain("02120000000");
    }

    [Theory]
    [InlineData("0212 000 00 00", "iptal için lütfen kliniği arayın: 0212 000 00 00")]
    [InlineData(null, "iptal için lütfen kliniği arayın.")]
    public void Body_Should_Ask_To_Call_Clinic_When_Cancel_Disabled(string? phone, string expected)
    {
        var body = AppointmentReminderEmail.Body("Ali Veli", "Pamuk", "Merkez", "09.11.2026 10:00", Links, cancelLinkEnabled: false, phone, CalendarLines);

        body.Should().Contain(Links.ConfirmUrl).And.NotContain(Links.CancelUrl).And.Contain(expected);
    }

    [Fact]
    public void Body_Should_End_With_Calendar_Links_And_No_Promotion()
    {
        var body = AppointmentReminderEmail.Body("Ali Veli", "Pamuk", "Merkez", "09.11.2026 10:00", Links, cancelLinkEnabled: true, null, CalendarLines);

        body.Should().EndWith(CalendarLines);
        body.Should().Contain("Google Takvim: https://calendar.google.com/calendar/render?action=TEMPLATE")
            .And.Contain("(.ics dosyası): https://api.test/api/portal/v1/appointment-link/calendar.ics?token=c");
        body.Should().NotContain("Tanıtım").And.NotContain("kampanya");
    }

    [Fact]
    public void Calendar_File_Url_Should_Use_Api_Root_And_Escape_Token()
        => AppointmentOwnerLinkUrls.CalendarFile(new AppOptions { BaseUrl = "https://api.test/" }, "a+b/c")
            .Should().Be("https://api.test/api/portal/v1/appointment-link/calendar.ics?token=a%2Bb%2Fc");

    [Fact]
    public void Page_Url_Should_Use_Portal_Root_And_Escape_Token()
    {
        var url = AppointmentOwnerLinkUrls.Page(
            new OwnerPortalOptions { PortalBaseUrl = "https://portal.test/" }, new AppOptions(), "a+b/c", AppointmentOwnerLinkAction.Cancel);

        url.Should().Be("https://portal.test/randevu-yanit?token=a%2Bb%2Fc&islem=iptal");
    }
}
