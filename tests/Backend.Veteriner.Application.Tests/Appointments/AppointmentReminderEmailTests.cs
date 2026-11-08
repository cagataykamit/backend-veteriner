using Backend.Veteriner.Application.Appointments.OwnerLinks;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Domain.Appointments;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Appointments;

/// <summary>TIER-019: hatırlatma e-postasındaki bağlantılar ve iptal kapalıyken "kliniği arayın" satırı.</summary>
public sealed class AppointmentReminderEmailTests
{
    private static readonly IssuedAppointmentOwnerLink Links = new(
        AppointmentOwnerLink.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "hash", DateTime.UtcNow.AddDays(1), DateTime.UtcNow),
        "https://portal.test/randevu-yanit?token=t&islem=onayla",
        "https://portal.test/randevu-yanit?token=t&islem=iptal");

    [Fact]
    public void Body_Should_Contain_Both_Links_When_Cancel_Enabled()
    {
        var body = AppointmentReminderEmail.Body("Ali Veli", "Pamuk", "Merkez", "09.11.2026 10:00", Links, cancelLinkEnabled: true, "02120000000");

        body.Should().StartWith("Sayın Ali Veli,\nPamuk için Merkez kliniğinde 09.11.2026 10:00 tarihinde planlanan randevunuzu hatırlatırız.");
        body.Should().Contain(Links.ConfirmUrl).And.Contain(Links.CancelUrl).And.Contain("yalnız bir kez");
        body.Should().NotContain("kliniği arayın").And.NotContain("02120000000");
    }

    [Theory]
    [InlineData("0212 000 00 00", "iptal için lütfen kliniği arayın: 0212 000 00 00")]
    [InlineData(null, "iptal için lütfen kliniği arayın.")]
    public void Body_Should_Ask_To_Call_Clinic_When_Cancel_Disabled(string? phone, string expected)
    {
        var body = AppointmentReminderEmail.Body("Ali Veli", "Pamuk", "Merkez", "09.11.2026 10:00", Links, cancelLinkEnabled: false, phone);

        body.Should().Contain(Links.ConfirmUrl).And.NotContain(Links.CancelUrl).And.Contain(expected);
    }

    [Fact]
    public void Page_Url_Should_Use_Portal_Root_And_Escape_Token()
    {
        var url = AppointmentOwnerLinkUrls.Page(
            new OwnerPortalOptions { PortalBaseUrl = "https://portal.test/" }, new AppOptions(), "a+b/c", AppointmentOwnerLinkAction.Cancel);

        url.Should().Be("https://portal.test/randevu-yanit?token=a%2Bb%2Fc&islem=iptal");
    }
}
