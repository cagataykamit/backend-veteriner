using Ardalis.Specification;
using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Payments.Specs;

public sealed class PaymentsFilteredPagedSpec : Specification<Payment>
{
    public PaymentsFilteredPagedSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid? clientId,
        Guid? petId,
        PaymentMethod? method,
        DateTime? paidFromUtc,
        DateTime? paidToUtc,
        int page,
        int pageSize)
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

        Query.OrderByDescending(p => p.PaidAtUtc)
            .ThenByDescending(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}
