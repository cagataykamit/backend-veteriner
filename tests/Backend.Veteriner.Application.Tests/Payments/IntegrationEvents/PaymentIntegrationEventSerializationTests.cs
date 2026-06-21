using System.Text.Json;
using Backend.Veteriner.Application.Payments.IntegrationEvents;
using Backend.Veteriner.Domain.Payments;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Payments.IntegrationEvents;

public sealed class PaymentIntegrationEventSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static PaymentProjectionSnapshot Snapshot()
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            150.50m,
            "TRY",
            (int)PaymentMethod.Cash,
            new DateTime(2026, 6, 20, 10, 0, 0, DateTimeKind.Utc),
            PaymentIntegrationEventTypes.SchemaVersion);

    [Fact]
    public void EventTypes_Should_BeUnique_And_FitOutboxColumn()
    {
        PaymentIntegrationEventTypes.All.Distinct(StringComparer.Ordinal)
            .Should().HaveCount(PaymentIntegrationEventTypes.All.Count);

        foreach (var type in PaymentIntegrationEventTypes.All)
        {
            type.Should().NotBeNullOrWhiteSpace();
            type.Length.Should().BeLessThanOrEqualTo(PaymentIntegrationEventTypes.MaxTypeLength);
            PaymentIntegrationEventTypes.IsKnown(type).Should().BeTrue();
        }
    }

    [Fact]
    public void Created_Should_RoundTrip()
    {
        var evt = new PaymentCreatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, Snapshot());

        var json = JsonSerializer.Serialize(evt, JsonOptions);
        var restored = JsonSerializer.Deserialize<PaymentCreatedIntegrationEvent>(json, JsonOptions);

        restored.Should().BeEquivalentTo(evt);
    }

    [Fact]
    public void Updated_Should_RoundTrip()
    {
        var evt = new PaymentUpdatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, Snapshot());

        var json = JsonSerializer.Serialize(evt, JsonOptions);
        var restored = JsonSerializer.Deserialize<PaymentUpdatedIntegrationEvent>(json, JsonOptions);

        restored.Should().BeEquivalentTo(evt);
    }

    [Fact]
    public void Snapshot_DecimalAmount_Should_PreservePrecision()
    {
        var snap = Snapshot() with { Amount = 1234567890123456.78m };

        var json = JsonSerializer.Serialize(snap, JsonOptions);
        var restored = JsonSerializer.Deserialize<PaymentProjectionSnapshot>(json, JsonOptions);

        restored!.Amount.Should().Be(1234567890123456.78m);
    }
}
