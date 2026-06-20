using System.Text.Json;
using Backend.Veteriner.Application.Pets.IntegrationEvents;
using Backend.Veteriner.Infrastructure.Outbox;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backend.IntegrationTests.Outbox;

public sealed class PetIntegrationEventInfrastructureTests
{
    private static PetProjectionSnapshot CreateSnapshot()
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Ayşe Yılmaz",
            "ayşe yılmaz",
            "Pamuk",
            "pamuk",
            Guid.NewGuid(),
            "Kedi",
            "kedi",
            null,
            "Tekir",
            null,
            Guid.NewGuid(),
            "Siyah",
            "siyah",
            2,
            new DateOnly(2024, 3, 10),
            4.25m);

    [Fact]
    public void EventTypes_Should_Be_Unique()
    {
        var distinct = PetIntegrationEventTypes.All.Distinct(StringComparer.Ordinal).ToList();
        distinct.Should().HaveCount(PetIntegrationEventTypes.All.Count);
    }

    [Theory]
    [MemberData(nameof(AllEventTypes))]
    public void EventTypes_Should_Fit_OutboxTypeColumn(string eventType)
    {
        eventType.Should().NotBeNullOrWhiteSpace();
        eventType.Length.Should().BeLessThanOrEqualTo(PetIntegrationEventTypes.MaxTypeLength);
    }

    [Theory]
    [MemberData(nameof(EventTypePayloadPairs))]
    public void Registry_Should_Resolve_KnownPayloadType(string eventType, Type expectedPayloadType)
    {
        PetIntegrationEventTypeRegistry.TryResolvePayloadType(eventType, out var payloadType)
            .Should().BeTrue();
        payloadType.Should().Be(expectedPayloadType);
    }

    [Fact]
    public void Registry_Should_Reject_UnknownType()
    {
        var actResolve = () => PetIntegrationEventTypeRegistry.ResolvePayloadType("pet.unknown.v1");
        actResolve.Should().Throw<UnknownPetIntegrationEventTypeException>();

        PetIntegrationEventTypeRegistry.TryResolvePayloadType("pet.unknown.v1", out _)
            .Should().BeFalse();
    }

    [Fact]
    public async Task Adapter_Should_Enqueue_TypeAndPayload_To_Buffer_Without_DatabaseAccess()
    {
        var buffer = new OutboxBuffer(NullLogger<OutboxBuffer>.Instance);
        var adapter = new PetIntegrationEventOutbox(buffer);

        var integrationEvent = new PetCreatedIntegrationEvent(
            Guid.NewGuid(),
            DateTime.UtcNow,
            CreateSnapshot());

        await adapter.EnqueueAsync(PetIntegrationEventTypes.Created, integrationEvent);

        var batch = buffer.Drain();
        batch.Should().ContainSingle();
        batch[0].Type.Should().Be(PetIntegrationEventTypes.Created);
        batch[0].AppointmentId.Should().BeNull();
        batch[0].AppointmentSequence.Should().BeNull();

        var restored = JsonSerializer.Deserialize<PetCreatedIntegrationEvent>(
            batch[0].Payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        restored.Should().BeEquivalentTo(integrationEvent);
    }

    [Fact]
    public async Task Adapter_Should_Reject_UnknownEventType()
    {
        var adapter = new PetIntegrationEventOutbox(new OutboxBuffer(NullLogger<OutboxBuffer>.Instance));

        var integrationEvent = new PetCreatedIntegrationEvent(
            Guid.NewGuid(),
            DateTime.UtcNow,
            CreateSnapshot());

        var act = () => adapter.EnqueueAsync("pet.unknown.v1", integrationEvent);

        await act.Should().ThrowAsync<UnknownPetIntegrationEventTypeException>();
    }

    [Fact]
    public async Task Adapter_Should_Reject_PayloadTypeMismatch()
    {
        var adapter = new PetIntegrationEventOutbox(new OutboxBuffer(NullLogger<OutboxBuffer>.Instance));

        var mismatched = new PetUpdatedIntegrationEvent(
            Guid.NewGuid(),
            DateTime.UtcNow,
            CreateSnapshot());

        var act = () => adapter.EnqueueAsync(PetIntegrationEventTypes.Created, mismatched);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("integrationEvent");
    }

    public static IEnumerable<object[]> AllEventTypes()
        => PetIntegrationEventTypes.All.Select(t => new object[] { t });

    public static IEnumerable<object[]> EventTypePayloadPairs()
    {
        yield return [PetIntegrationEventTypes.Created, typeof(PetCreatedIntegrationEvent)];
        yield return [PetIntegrationEventTypes.Updated, typeof(PetUpdatedIntegrationEvent)];
    }
}
