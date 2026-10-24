using Backend.Veteriner.Application.Pets.IntegrationEvents;
using Backend.Veteriner.Application.Projections.Pets;
using Backend.Veteriner.Infrastructure.Persistence;

namespace Backend.Veteriner.Infrastructure.Outbox;

/// <summary>Hasta projeksiyonu olayları için claim deposu; mantık <see cref="SqlIntegrationOutboxClaimRepository"/>'de.</summary>
public sealed class SqlPetOutboxClaimRepository : SqlIntegrationOutboxClaimRepository, IPetOutboxClaimRepository
{
    public SqlPetOutboxClaimRepository(AppDbContext dbContext, TimeProvider timeProvider)
        : base(dbContext, timeProvider, PetIntegrationEventTypes.All.ToArray())
    {
    }
}
