using Ardalis.Specification;
using Backend.Veteriner.Domain.RecordHistory;

namespace Backend.Veteriner.Application.RecordHistory.Specs;

/// <summary>Bir kaydın geçmişi, yeniden eskiye; en çok <paramref name="take"/> satır.</summary>
public sealed class RecordChangesByRecordSpec : Specification<RecordChange>
{
    public RecordChangesByRecordSpec(Guid tenantId, string recordType, Guid recordId, int take)
    {
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && c.RecordType == recordType && c.RecordId == recordId);
        Query.OrderByDescending(c => c.OccurredAtUtc).ThenByDescending(c => c.Id);
        Query.Take(take);
    }
}
