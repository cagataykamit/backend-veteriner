using Ardalis.Specification;
using Backend.Veteriner.Domain.Charges;

namespace Backend.Veteriner.Application.Charges.Specs;

/// <summary>İzlenen (tracked) ücret; düzeltme/iptal uçları için. İptal edilmiş kayıt da gelir (<c>Charges.AlreadyVoided</c> ayrımı için).</summary>
public sealed class ChargeForUpdateSpec : Specification<Charge>
{
    public ChargeForUpdateSpec(Guid tenantId, Guid id)
    {
        Query.IgnoreQueryFilters();
        Query.Where(c => c.TenantId == tenantId && c.Id == id);
    }
}
