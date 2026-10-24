using Backend.Veteriner.Application.Clients.IntegrationEvents;
using Backend.Veteriner.Application.Projections.Clients;
using Backend.Veteriner.Infrastructure.Persistence;

namespace Backend.Veteriner.Infrastructure.Outbox;

/// <summary>Müşteri projeksiyonu olayları için claim deposu; mantık <see cref="SqlIntegrationOutboxClaimRepository"/>'de.</summary>
public sealed class SqlClientOutboxClaimRepository : SqlIntegrationOutboxClaimRepository, IClientOutboxClaimRepository
{
    public SqlClientOutboxClaimRepository(AppDbContext dbContext, TimeProvider timeProvider)
        : base(dbContext, timeProvider, ClientIntegrationEventTypes.All.ToArray())
    {
    }
}
