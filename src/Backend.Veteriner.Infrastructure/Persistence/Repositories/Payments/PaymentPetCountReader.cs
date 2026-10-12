using Backend.Veteriner.Application.Payments.Queries;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories.Payments;

public sealed class PaymentPetCountReader : IPaymentPetCountReader
{
    private readonly AppDbContext _db;

    public PaymentPetCountReader(AppDbContext db)
        => _db = db;

    public async Task<IReadOnlyDictionary<Guid, int>> CountDistinctPetsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> paymentIds, CancellationToken ct)
    {
        if (paymentIds.Count == 0)
            return new Dictionary<Guid, int>();

        var pairs = await (
                from a in _db.PaymentAllocations.AsNoTracking()
                join c in _db.Charges.AsNoTracking() on a.ChargeId equals c.Id
                where a.TenantId == tenantId && paymentIds.Contains(a.PaymentId) && c.PetId != null
                select new { a.PaymentId, PetId = c.PetId!.Value })
            .Distinct()
            .ToListAsync(ct);

        return pairs.GroupBy(x => x.PaymentId).ToDictionary(g => g.Key, g => g.Count());
    }
}