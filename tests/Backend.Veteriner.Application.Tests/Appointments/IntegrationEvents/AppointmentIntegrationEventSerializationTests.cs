using System.Text.Json;
using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Appointments.IntegrationEvents;

public sealed class AppointmentIntegrationEventSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void CreatedEvent_Should_RoundTrip_Through_Json()
    {
        var original = new AppointmentCreatedIntegrationEvent(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            new DateTime(2026, 6, 16, 10, 30, 0, DateTimeKind.Utc),
            CreateSnapshot(
                appointmentId: Guid.Parse("11111111-2222-3333-4444-555555555555"),
                notes: "Kontrol randevusu",
                clientPhone: "+905551112233"));

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var restored = JsonSerializer.Deserialize<AppointmentCreatedIntegrationEvent>(json, JsonOptions);

        restored.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void UpdatedEvent_Should_RoundTrip_PreviousAndCurrent()
    {
        var previous = CreateSnapshot(
            appointmentId: Guid.Parse("22222222-3333-4444-5555-666666666666"),
            scheduledAtUtc: new DateTime(2026, 6, 16, 9, 0, 0, DateTimeKind.Utc),
            status: 0,
            notes: null,
            clientPhone: null);

        var current = CreateSnapshot(
            appointmentId: Guid.Parse("22222222-3333-4444-5555-666666666666"),
            scheduledAtUtc: new DateTime(2026, 6, 16, 11, 0, 0, DateTimeKind.Utc),
            status: 0,
            notes: "Erteleme notu",
            clientPhone: "+905559998877");

        var original = new AppointmentUpdatedIntegrationEvent(
            Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff"),
            new DateTime(2026, 6, 16, 8, 15, 0, DateTimeKind.Utc),
            previous,
            current);

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var restored = JsonSerializer.Deserialize<AppointmentUpdatedIntegrationEvent>(json, JsonOptions);

        restored.Should().BeEquivalentTo(original);
        restored!.Previous.ClientPhone.Should().BeNull();
        restored.Previous.Notes.Should().BeNull();
        restored.Current.ClientPhone.Should().Be("+905559998877");
        restored.Current.ScheduledAtUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void RescheduledEvent_Should_Preserve_Guid_And_Utc_Values()
    {
        var previous = CreateSnapshot(
            appointmentId: Guid.Parse("33333333-4444-5555-6666-777777777777"),
            scheduledAtUtc: new DateTime(2026, 6, 17, 7, 30, 0, DateTimeKind.Utc),
            durationMinutes: 30);

        var current = CreateSnapshot(
            appointmentId: Guid.Parse("33333333-4444-5555-6666-777777777777"),
            scheduledAtUtc: new DateTime(2026, 6, 17, 8, 30, 0, DateTimeKind.Utc),
            durationMinutes: 45);

        var original = new AppointmentRescheduledIntegrationEvent(
            Guid.NewGuid(),
            DateTime.UtcNow,
            previous,
            current);

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var restored = JsonSerializer.Deserialize<AppointmentRescheduledIntegrationEvent>(json, JsonOptions);

        restored.Should().BeEquivalentTo(original);
        restored!.Previous.AppointmentId.Should().Be(restored.Current.AppointmentId);
        restored.Previous.ScheduledAtUtc.Should().Be(previous.ScheduledAtUtc);
        restored.Current.DurationMinutes.Should().Be(45);
    }

    private static AppointmentProjectionSnapshot CreateSnapshot(
        Guid appointmentId,
        DateTime? scheduledAtUtc = null,
        int durationMinutes = 30,
        int appointmentType = 0,
        int status = 0,
        string? notes = "not",
        string? clientPhone = "+905551234567")
    {
        return new AppointmentProjectionSnapshot(
            appointmentId,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "Varsayilan Klinik",
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            "Pamuk",
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            "Kedi",
            Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            "Ayse Yilmaz",
            clientPhone,
            scheduledAtUtc ?? new DateTime(2026, 6, 16, 12, 0, 0, DateTimeKind.Utc),
            durationMinutes,
            appointmentType,
            status,
            notes);
    }
}
