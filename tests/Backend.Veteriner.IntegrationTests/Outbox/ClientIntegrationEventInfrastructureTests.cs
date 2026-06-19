using System.Text.Json;
using Backend.Veteriner.Application.Clients.IntegrationEvents;
using Backend.Veteriner.Infrastructure.Outbox;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backend.IntegrationTests.Outbox;

public sealed class ClientIntegrationEventInfrastructureTests
{
    private static ClientProjectionSnapshot CreateSnapshot()
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Ayşe Yılmaz",
            "ayşe yılmaz",
            "ayse@example.com",
            "905321234567",
            "905321234567",
            new DateTime(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void EventTypes_Should_Be_Unique()
    {
        var distinct = ClientIntegrationEventTypes.All.Distinct(StringComparer.Ordinal).ToList();
        distinct.Should().HaveCount(ClientIntegrationEventTypes.All.Count);
    }

    [Theory]
    [MemberData(nameof(AllEventTypes))]
    public void EventTypes_Should_Fit_OutboxTypeColumn(string eventType)
    {
        eventType.Should().NotBeNullOrWhiteSpace();
        eventType.Length.Should().BeLessThanOrEqualTo(ClientIntegrationEventTypes.MaxTypeLength);
    }

    [Theory]
    [MemberData(nameof(EventTypePayloadPairs))]
    public void Registry_Should_Resolve_KnownPayloadType(string eventType, Type expectedPayloadType)
    {
        ClientIntegrationEventTypeRegistry.TryResolvePayloadType(eventType, out var payloadType)
            .Should().BeTrue();
        payloadType.Should().Be(expectedPayloadType);
    }

    [Fact]
    public void Registry_Should_Reject_UnknownType()
    {
        var actResolve = () => ClientIntegrationEventTypeRegistry.ResolvePayloadType("client.unknown.v1");
        actResolve.Should().Throw<UnknownClientIntegrationEventTypeException>();

        ClientIntegrationEventTypeRegistry.TryResolvePayloadType("client.unknown.v1", out _)
            .Should().BeFalse();
    }

    [Fact]
    public async Task Adapter_Should_Enqueue_TypeAndPayload_To_Buffer_Without_DatabaseAccess()
    {
        var buffer = new OutboxBuffer(NullLogger<OutboxBuffer>.Instance);
        var adapter = new ClientIntegrationEventOutbox(buffer);

        var integrationEvent = new ClientCreatedIntegrationEvent(
            Guid.NewGuid(),
            DateTime.UtcNow,
            CreateSnapshot());

        await adapter.EnqueueAsync(ClientIntegrationEventTypes.Created, integrationEvent);

        var batch = buffer.Drain();
        batch.Should().ContainSingle();
        batch[0].Type.Should().Be(ClientIntegrationEventTypes.Created);
        batch[0].AppointmentId.Should().BeNull();
        batch[0].AppointmentSequence.Should().BeNull();

        var restored = JsonSerializer.Deserialize<ClientCreatedIntegrationEvent>(
            batch[0].Payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        restored.Should().BeEquivalentTo(integrationEvent);
    }

    [Fact]
    public async Task Adapter_Should_Reject_UnknownEventType()
    {
        var adapter = new ClientIntegrationEventOutbox(new OutboxBuffer(NullLogger<OutboxBuffer>.Instance));

        var integrationEvent = new ClientCreatedIntegrationEvent(
            Guid.NewGuid(),
            DateTime.UtcNow,
            CreateSnapshot());

        var act = () => adapter.EnqueueAsync("client.unknown.v1", integrationEvent);

        await act.Should().ThrowAsync<UnknownClientIntegrationEventTypeException>();
    }

    [Fact]
    public async Task Adapter_Should_Reject_EventTypeLongerThan64Characters()
    {
        var adapter = new ClientIntegrationEventOutbox(new OutboxBuffer(NullLogger<OutboxBuffer>.Instance));

        var integrationEvent = new ClientCreatedIntegrationEvent(
            Guid.NewGuid(),
            DateTime.UtcNow,
            CreateSnapshot());

        var tooLong = new string('a', ClientIntegrationEventTypes.MaxTypeLength + 1);
        var act = () => adapter.EnqueueAsync(tooLong, integrationEvent);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("eventType");
    }

    [Fact]
    public async Task Adapter_Should_Reject_PayloadTypeMismatch()
    {
        var adapter = new ClientIntegrationEventOutbox(new OutboxBuffer(NullLogger<OutboxBuffer>.Instance));

        // Created type ile Updated payload uyumsuzluğu reddedilmeli.
        var mismatched = new ClientUpdatedIntegrationEvent(
            Guid.NewGuid(),
            DateTime.UtcNow,
            CreateSnapshot());

        var act = () => adapter.EnqueueAsync(ClientIntegrationEventTypes.Created, mismatched);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("integrationEvent");
    }

    public static IEnumerable<object[]> AllEventTypes()
        => ClientIntegrationEventTypes.All.Select(t => new object[] { t });

    public static IEnumerable<object[]> EventTypePayloadPairs()
    {
        yield return [ClientIntegrationEventTypes.Created, typeof(ClientCreatedIntegrationEvent)];
        yield return [ClientIntegrationEventTypes.Updated, typeof(ClientUpdatedIntegrationEvent)];
    }
}
