using Ardalis.Specification;
using Backend.Veteriner.Domain.Payments;
using Microsoft.EntityFrameworkCore;

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
        int pageSize,
        string? searchContainsLikePattern,
        Guid[] searchMatchClientIds,
        Guid[] searchMatchPetIds)
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

        if (searchContainsLikePattern is not null)
        {
            var pattern = searchContainsLikePattern;
            var cids = searchMatchClientIds;
            var pids = searchMatchPetIds;
            Query.Where(p =>
                (p.Notes != null && EF.Functions.Like(p.Notes, pattern)) ||
                EF.Functions.Like(p.Currency, pattern) ||
                (cids.Length > 0 && cids.Contains(p.ClientId)) ||
                (p.PetId != null && pids.Length > 0 && pids.Contains(p.PetId.Value)));
        }

        Query.OrderByDescending(p => p.PaidAtUtc)
            .ThenByDescending(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}
