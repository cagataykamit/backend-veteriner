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

    [Fact]
    public void LegacyV1SnapshotJson_WithoutSearchFields_Should_DeserializeWithNullDefaults()
    {
        const string legacyJson = """
            {
              "appointmentId":"11111111-2222-3333-4444-555555555555",
              "tenantId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
              "clinicId":"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
              "clinicName":"Varsayilan Klinik",
              "petId":"cccccccc-cccc-cccc-cccc-cccccccccccc",
              "petName":"Pamuk",
              "speciesId":"dddddddd-dddd-dddd-dddd-dddddddddddd",
              "speciesName":"Kedi",
              "clientId":"eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
              "clientName":"Ayse Yilmaz",
              "clientPhone":"+905551234567",
              "scheduledAtUtc":"2026-06-16T12:00:00Z",
              "durationMinutes":30,
              "appointmentType":0,
              "status":0,
              "notes":"not"
            }
            """;

        var restored = JsonSerializer.Deserialize<AppointmentProjectionSnapshot>(legacyJson, JsonOptions);

        restored.Should().NotBeNull();
        restored!.PetBreed.Should().BeNull();
        restored.PetBreedRefName.Should().BeNull();
        restored.ClientEmail.Should().BeNull();
        restored.ClientPhoneNormalized.Should().BeNull();
    }

    [Fact]
    public void Snapshot_WithSearchFields_Should_RoundTrip_Through_Json()
    {
        var original = CreateSnapshot(
            appointmentId: Guid.Parse("11111111-2222-3333-4444-555555555555"),
            petBreed: "Golden Retriever",
            petBreedRefName: "Labrador",
            clientEmail: "owner@example.com",
            clientPhoneNormalized: "905551112233");

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var restored = JsonSerializer.Deserialize<AppointmentProjectionSnapshot>(json, JsonOptions);

        restored.Should().BeEquivalentTo(original);
        restored!.PetBreed.Should().Be("Golden Retriever");
        restored.ClientEmail.Should().Be("owner@example.com");
    }

    private static AppointmentProjectionSnapshot CreateSnapshot(
        Guid appointmentId,
        DateTime? scheduledAtUtc = null,
        int durationMinutes = 30,
        int appointmentType = 0,
        int status = 0,
        string? notes = "not",
        string? clientPhone = "+905551234567",
        string? petBreed = null,
        string? petBreedRefName = null,
        string? clientEmail = null,
        string? clientPhoneNormalized = null)
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
            notes,
            petBreed,
            petBreedRefName,
            clientEmail,
            clientPhoneNormalized);
    }
}
