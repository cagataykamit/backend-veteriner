using Backend.Veteriner.Domain.DemoRequests;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.DemoRequests;

/// <summary>TRIAL-005: kırpma, boş isteğe bağlı alanın null olması, uzunluk sınırı ve bildirim işareti.</summary>
public sealed class DemoRequestTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc);

    private static DemoRequest Create(string? city = " İzmir ", string? phone = "  ", string? message = null)
        => DemoRequest.Create(" Ayşe Yılmaz ", " Işık Veteriner ", city, " ayse@example.com ", phone,
            DemoClinicSize.MultiBranch, null, message, Now);

    [Fact]
    public void Create_Should_Trim_And_Null_Empty_Optional_Fields()
    {
        var request = Create();

        request.ContactName.Should().Be("Ayşe Yılmaz");
        request.ClinicName.Should().Be("Işık Veteriner");
        request.City.Should().Be("İzmir");
        request.Email.Should().Be("ayse@example.com");
        request.Phone.Should().BeNull();
        request.CurrentSoftware.Should().BeNull();
        request.ClinicSize.Should().Be(DemoClinicSize.MultiBranch);
        request.CreatedAtUtc.Should().Be(Now);
        request.NotificationQueuedAtUtc.Should().BeNull();
    }

    [Fact]
    public void Create_Should_Reject_Too_Long_Message()
    {
        var act = () => Create(message: new string('a', DemoRequest.MaxMessageLength + 1));

        act.Should().Throw<ArgumentException>().WithParameterName("message");
    }

    [Fact]
    public void Create_Should_Reject_Undefined_Clinic_Size()
    {
        var act = () => DemoRequest.Create("Ayşe", "Klinik", null, "a@example.com", null, (DemoClinicSize)99, null, null, Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void MarkNotificationQueued_Should_Write_Only_Once()
    {
        var request = Create();

        request.MarkNotificationQueued(Now);
        request.MarkNotificationQueued(Now.AddHours(1));

        request.NotificationQueuedAtUtc.Should().Be(Now);
    }
}
