using Ardalis.Specification;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Application.Common.Specifications;

namespace Backend.Veteriner.Application.Charges.Specs;

/// <summary>Müşteri bakiyesi için müşterinin klinik kapsamındaki tüm ücretleri (sayaçlarıyla) okur.</summary>
public sealed class ChargesForClientBalanceSpec : Specification<Charge>
{
    public ChargesForClientBalanceSpec(
        Guid tenantId,
        Guid clientId,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds)
    {
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && c.ClientId == clientId);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
    }
}
