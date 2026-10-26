using System.Text.Json;
using Backend.Veteriner.Application.Pets.IntegrationEvents;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Pets.IntegrationEvents;

public sealed class PetIntegrationEventSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static PetProjectionSnapshot Snapshot()
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
    public void EventTypes_Should_BeUnique_And_FitOutboxColumn()
    {
        PetIntegrationEventTypes.All.Distinct(StringComparer.Ordinal)
            .Should().HaveCount(PetIntegrationEventTypes.All.Count);

        foreach (var type in PetIntegrationEventTypes.All)
        {
            type.Should().NotBeNullOrWhiteSpace();
            type.Length.Should().BeLessThanOrEqualTo(PetIntegrationEventTypes.MaxTypeLength);
            PetIntegrationEventTypes.IsKnown(type).Should().BeTrue();
        }
    }

    [Fact]
    public void Created_Should_RoundTrip()
    {
        var evt = new PetCreatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, Snapshot());

        var json = JsonSerializer.Serialize(evt, JsonOptions);
        var restored = JsonSerializer.Deserialize<PetCreatedIntegrationEvent>(json, JsonOptions);

        restored.Should().BeEquivalentTo(evt);
    }

    [Fact]
    public void Deleted_Should_RoundTrip_Without_Personal_Data()
    {
        var evt = new PetDeletedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid());

        var json = JsonSerializer.Serialize(evt, JsonOptions);
        var restored = JsonSerializer.Deserialize<PetDeletedIntegrationEvent>(json, JsonOptions);

        restored.Should().BeEquivalentTo(evt);
        PetIntegrationEventTypes.Deleted.Should().Be("pet.deleted.v1");
    }

    [Fact]
    public void Updated_Should_RoundTrip()
    {
        var evt = new PetUpdatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, Snapshot());

        var json = JsonSerializer.Serialize(evt, JsonOptions);
        var restored = JsonSerializer.Deserialize<PetUpdatedIntegrationEvent>(json, JsonOptions);

        restored.Should().BeEquivalentTo(evt);
    }
}
