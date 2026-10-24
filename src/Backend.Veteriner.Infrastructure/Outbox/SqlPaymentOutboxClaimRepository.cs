using Backend.Veteriner.Application.Payments.IntegrationEvents;
using Backend.Veteriner.Application.Projections.Payments;
using Backend.Veteriner.Infrastructure.Persistence;

namespace Backend.Veteriner.Infrastructure.Outbox;

/// <summary>Ödeme projeksiyonu olayları için claim deposu; mantık <see cref="SqlIntegrationOutboxClaimRepository"/>'de.</summary>
public sealed class SqlPaymentOutboxClaimRepository : SqlIntegrationOutboxClaimRepository, IPaymentOutboxClaimRepository
{
    public SqlPaymentOutboxClaimRepository(AppDbContext dbContext, TimeProvider timeProvider)
        : base(dbContext, timeProvider, PaymentIntegrationEventTypes.All.ToArray())
    {
    }
}
