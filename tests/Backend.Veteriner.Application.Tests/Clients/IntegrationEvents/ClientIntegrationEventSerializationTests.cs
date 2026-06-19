using System.Text.Json;
using Backend.Veteriner.Application.Clients.IntegrationEvents;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Clients.IntegrationEvents;

public sealed class ClientIntegrationEventSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static ClientProjectionSnapshot Snapshot()
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
    public void EventTypes_Should_BeUnique_And_FitOutboxColumn()
    {
        ClientIntegrationEventTypes.All.Distinct(StringComparer.Ordinal)
            .Should().HaveCount(ClientIntegrationEventTypes.All.Count);

        foreach (var type in ClientIntegrationEventTypes.All)
        {
            type.Should().NotBeNullOrWhiteSpace();
            type.Length.Should().BeLessThanOrEqualTo(ClientIntegrationEventTypes.MaxTypeLength);
            ClientIntegrationEventTypes.IsKnown(type).Should().BeTrue();
        }
    }

    [Fact]
    public void Created_Should_RoundTrip()
    {
        var evt = new ClientCreatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, Snapshot());

        var json = JsonSerializer.Serialize(evt, JsonOptions);
        var restored = JsonSerializer.Deserialize<ClientCreatedIntegrationEvent>(json, JsonOptions);

        restored.Should().BeEquivalentTo(evt);
    }

    [Fact]
    public void Updated_Should_RoundTrip()
    {
        var evt = new ClientUpdatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, Snapshot());

        var json = JsonSerializer.Serialize(evt, JsonOptions);
        var restored = JsonSerializer.Deserialize<ClientUpdatedIntegrationEvent>(json, JsonOptions);

        restored.Should().BeEquivalentTo(evt);
    }
}
