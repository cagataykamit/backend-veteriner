using Backend.IntegrationTests.Projections.Payments;
using Backend.Veteriner.Application.Payments.IntegrationEvents;
using Backend.Veteriner.Application.Pets.IntegrationEvents;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Projections.Payments;
using Backend.Veteriner.Infrastructure.Projections.Pets;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Outbox;

/// <summary>
/// Kullanılmayan projeksiyonun olayları yazılmaz (<c>WriteEvents = false</c>): tablo boşuna büyümez, olay yine doğrulanır.
/// Okuma modeli sonradan backfill ile kurulur.
/// </summary>
public sealed class ClosedProjectionEventWriteTests
{
    [Fact]
    public async Task Pet_Events_Should_Not_Be_Written_When_WriteEvents_Is_Off_But_Still_Be_Validated()
    {
        var buffer = new OutboxBuffer(NullLogger<OutboxBuffer>.Instance);
        var adapter = new PetIntegrationEventOutbox(buffer, Options.Create(new PetProjectionOptions { WriteEvents = false }));

        await adapter.EnqueueAsync(PetIntegrationEventTypes.Deleted, new PetDeletedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid()));

        buffer.Drain().Should().BeEmpty();
        var unknown = () => adapter.EnqueueAsync("pet.unknown.v1", new object());
        await unknown.Should().ThrowAsync<UnknownPetIntegrationEventTypeException>();
    }

    [Fact]
    public async Task Payment_Events_Should_Not_Be_Written_When_WriteEvents_Is_Off()
    {
        var buffer = new OutboxBuffer(NullLogger<OutboxBuffer>.Instance);
        var adapter = new PaymentIntegrationEventOutbox(buffer, Options.Create(new PaymentProjectionOptions { WriteEvents = false }));
        var snapshot = PaymentProjectionTestSupport.CreateSnapshot(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await adapter.EnqueueAsync(PaymentIntegrationEventTypes.Created, new PaymentCreatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, snapshot));

        buffer.Drain().Should().BeEmpty();
    }

    [Fact]
    public void WriteEvents_Should_Default_To_True_So_Unconfigured_Environments_Keep_Events()
    {
        new PetProjectionOptions().WriteEvents.Should().BeTrue();
        new PaymentProjectionOptions().WriteEvents.Should().BeTrue();
    }
}
