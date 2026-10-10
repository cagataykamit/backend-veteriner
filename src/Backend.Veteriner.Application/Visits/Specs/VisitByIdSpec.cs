using Ardalis.Specification;
using Backend.Veteriner.Domain.Visits;

namespace Backend.Veteriner.Application.Visits.Specs;

/// <summary>
/// Yazma akışları izlenen varlık ister; eşzamanlılık sonrası yeniden okuma ve sorgular
/// <paramref name="asNoTracking"/> ile veritabanındaki güncel değeri alır.
/// </summary>
public sealed class VisitByIdSpec : Specification<Visit>
{
    public VisitByIdSpec(Guid tenantId, Guid id, bool asNoTracking = false)
    {
        Query.Where(v => v.TenantId == tenantId && v.Id == id);
        if (asNoTracking)
            Query.AsNoTracking();
    }
}
