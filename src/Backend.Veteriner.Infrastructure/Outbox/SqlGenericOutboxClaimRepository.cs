using Backend.Veteriner.Infrastructure.Persistence;

namespace Backend.Veteriner.Infrastructure.Outbox;

/// <summary>
/// Generic outbox işlemcisi (<see cref="OutboxProcessor"/>) için claim deposu: projeksiyon olayları dışındaki tüm tipleri
/// alır; tanınmayan tip de alınır ve işlemcide retry/dead-letter'a düşer. Mantık <see cref="SqlIntegrationOutboxClaimRepository"/>'de.
/// </summary>
public sealed class SqlGenericOutboxClaimRepository : SqlIntegrationOutboxClaimRepository
{
    public SqlGenericOutboxClaimRepository(AppDbContext dbContext, TimeProvider timeProvider)
        : base(dbContext, timeProvider, OutboxMessageQueryFilters.ProjectionIntegrationEventTypes, excludeEventTypes: true)
    {
    }
}
