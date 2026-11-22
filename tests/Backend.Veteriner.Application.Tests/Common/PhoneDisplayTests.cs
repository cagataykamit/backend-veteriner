using Backend.Veteriner.Application.AppointmentRequests;
using Backend.Veteriner.Application.Common;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Common;

/// <summary>E-postalarda klinik telefonu panel, portal ve siteyle aynı biçimde gider (tam test 2026-11-22: "02321234567" biçimsizdi).</summary>
public sealed class PhoneDisplayTests
{
    [Theory]
    [InlineData("05439580091", "+90 543 958 00 91")]
    [InlineData("905439580091", "+90 543 958 00 91")]
    [InlineData("5439580091", "+90 543 958 00 91")]
    [InlineData("02321234567", "+90 232 123 45 67")]
    [InlineData("0850 123 45 67", "+90 850 123 45 67")]
    [InlineData(" +44 20 7946 0958 ", "+44 20 7946 0958")]
    [InlineData("112", "112")]
    [InlineData(null, null)]
    [InlineData("  ", "")]
    public void Format_Should_Match_Panel_Portal_And_Site(string? input, string? expected)
        => PhoneDisplay.Format(input).Should().Be(expected);

    [Fact]
    public void Appointment_Request_Email_Should_Write_Formatted_Clinic_Phone()
    {
        var values = new AppointmentRequestEmailValues("Ayşe", "Merkez Pati", "02321234567", new DateTime(2026, 11, 22, 9, 0, 0, DateTimeKind.Utc), "Aşı", null, null);

        AppointmentRequestEmails.Render("Konu", "Bize {{clinicPhone}} numarasından ulaşın.", values).Body
            .Should().Be("Bize +90 232 123 45 67 numarasından ulaşın.");
    }
}
