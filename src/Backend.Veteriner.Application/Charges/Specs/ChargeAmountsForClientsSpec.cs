using Ardalis.Specification;
using Backend.Veteriner.Domain.Charges;

namespace Backend.Veteriner.Application.Charges.Specs;

public sealed record ChargeAmountsRow(Guid ClientId, Guid? VisitId, decimal Amount, decimal AllocatedAmount);

/// <summary>Bugün satırları için bir kliniğin, verilen müşterilere ait ücret tutarları (tek sorgu; ziyaret ve müşteri toplamı bellekte).</summary>
public sealed class ChargeAmountsForClientsSpec : Specification<Charge, ChargeAmountsRow>
{
    public ChargeAmountsForClientsSpec(Guid tenantId, Guid clinicId, IReadOnlyCollection<Guid> clientIds)
    {
        Query.Where(c => c.TenantId == tenantId && c.ClinicId == clinicId && clientIds.Contains(c.ClientId));
        Query.Select(c => new ChargeAmountsRow(c.ClientId, c.VisitId, c.Amount, c.AllocatedAmount));
    }
}
