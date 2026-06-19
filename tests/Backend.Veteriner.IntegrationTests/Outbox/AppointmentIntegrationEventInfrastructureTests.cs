using System.Text.Json;
using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Common.Outbox;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backend.IntegrationTests.Outbox;

public sealed class AppointmentIntegrationEventInfrastructureTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AppointmentIntegrationEventInfrastructureTests(CustomWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public void EventTypes_Should_Be_Unique()
    {
        var distinct = AppointmentIntegrationEventTypes.All.Distinct(StringComparer.Ordinal).ToList();
        distinct.Should().HaveCount(AppointmentIntegrationEventTypes.All.Count);
    }

    [Theory]
    [MemberData(nameof(AllEventTypes))]
    public void EventTypes_Should_Fit_OutboxTypeColumn(string eventType)
    {
        eventType.Should().NotBeNullOrWhiteSpace();
        eventType.Length.Should().BeLessThanOrEqualTo(AppointmentIntegrationEventTypes.MaxTypeLength);
    }

    [Theory]
    [MemberData(nameof(EventTypePayloadPairs))]
    public void Registry_Should_Resolve_KnownPayloadType(string eventType, Type expectedPayloadType)
    {
        AppointmentIntegrationEventTypeRegistry.TryResolvePayloadType(eventType, out var payloadType)
            .Should().BeTrue();
        payloadType.Should().Be(expectedPayloadType);
    }

    [Fact]
    public void Registry_Should_Reject_UnknownType()
    {
        var actResolve = () => AppointmentIntegrationEventTypeRegistry.ResolvePayloadType("appointment.unknown.v1");
        actResolve.Should().Throw<UnknownAppointmentIntegrationEventTypeException>();

        AppointmentIntegrationEventTypeRegistry.TryResolvePayloadType("appointment.unknown.v1", out _)
            .Should().BeFalse();
    }

    [Fact]
    public async Task Adapter_Should_Enqueue_TypeAndPayload_To_Buffer_Without_DatabaseAccess()
    {
        var buffer = new OutboxBuffer(NullLogger<OutboxBuffer>.Instance);
        var adapter = new AppointmentIntegrationEventOutbox(buffer);

        var integrationEvent = new AppointmentCreatedIntegrationEvent(
            Guid.NewGuid(),
            DateTime.UtcNow,
            1L,
            CreateSnapshot());

        await adapter.EnqueueAsync(AppointmentIntegrationEventTypes.Created, integrationEvent);

        var batch = buffer.Drain();
        batch.Should().ContainSingle();
        batch[0].Type.Should().Be(AppointmentIntegrationEventTypes.Created);
        batch[0].AppointmentId.Should().Be(integrationEvent.AppointmentId);
        batch[0].AppointmentSequence.Should().Be(integrationEvent.AppointmentSequence);

        var restored = JsonSerializer.Deserialize<AppointmentCreatedIntegrationEvent>(
            batch[0].Payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        restored.Should().BeEquivalentTo(integrationEvent);
    }

    [Fact]
    public async Task Adapter_Should_Reject_UnknownEventType()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var adapter = scope.ServiceProvider.GetRequiredService<IAppointmentIntegrationEventOutbox>();

        var integrationEvent = new AppointmentCreatedIntegrationEvent(
            Guid.NewGuid(),
            DateTime.UtcNow,
            1L,
            CreateSnapshot());

        var act = () => adapter.EnqueueAsync("appointment.unknown.v1", integrationEvent);

        await act.Should().ThrowAsync<UnknownAppointmentIntegrationEventTypeException>();
    }

    [Fact]
    public async Task Adapter_Should_Reject_EventTypeLongerThan64Characters()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var adapter = scope.ServiceProvider.GetRequiredService<IAppointmentIntegrationEventOutbox>();

        var integrationEvent = new AppointmentCreatedIntegrationEvent(
            Guid.NewGuid(),
            DateTime.UtcNow,
            1L,
            CreateSnapshot());

        var tooLong = new string('a', AppointmentIntegrationEventTypes.MaxTypeLength + 1);
        var act = () => adapter.EnqueueAsync(tooLong, integrationEvent);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("eventType");
    }

    public static IEnumerable<object[]> AllEventTypes()
        => AppointmentIntegrationEventTypes.All.Select(t => new object[] { t });

    public static IEnumerable<object[]> EventTypePayloadPairs()
    {
        yield return [AppointmentIntegrationEventTypes.Created, typeof(AppointmentCreatedIntegrationEvent)];
        yield return [AppointmentIntegrationEventTypes.Updated, typeof(AppointmentUpdatedIntegrationEvent)];
        yield return [AppointmentIntegrationEventTypes.Rescheduled, typeof(AppointmentRescheduledIntegrationEvent)];
        yield return [AppointmentIntegrationEventTypes.Cancelled, typeof(AppointmentCancelledIntegrationEvent)];
        yield return [AppointmentIntegrationEventTypes.Completed, typeof(AppointmentCompletedIntegrationEvent)];
    }

    private static AppointmentProjectionSnapshot CreateSnapshot()
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Test Klinik",
            Guid.NewGuid(),
            "Boncuk",
            Guid.NewGuid(),
            "Kopek",
            Guid.NewGuid(),
            "Test Musteri",
            "+905551112233",
            new DateTime(2026, 6, 16, 14, 0, 0, DateTimeKind.Utc),
            30,
            0,
            0,
            "Test notu");
}
