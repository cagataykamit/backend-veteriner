using Ardalis.Specification;
using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Payments.Specs;

public sealed class PaymentsFilteredCountSpec : Specification<Payment>
{
    public PaymentsFilteredCountSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid? clientId,
        Guid? petId,
        PaymentMethod? method,
        DateTime? paidFromUtc,
        DateTime? paidToUtc)
    {
        Query.Where(p => p.TenantId == tenantId);
        if (clinicId.HasValue)
            Query.Where(p => p.ClinicId == clinicId.Value);
        if (clientId.HasValue)
            Query.Where(p => p.ClientId == clientId.Value);
        if (petId.HasValue)
            Query.Where(p => p.PetId == petId.Value);
        if (method.HasValue)
            Query.Where(p => p.Method == method.Value);
        if (paidFromUtc.HasValue)
            Query.Where(p => p.PaidAtUtc >= paidFromUtc.Value);
        if (paidToUtc.HasValue)
            Query.Where(p => p.PaidAtUtc <= paidToUtc.Value);
    }
}
