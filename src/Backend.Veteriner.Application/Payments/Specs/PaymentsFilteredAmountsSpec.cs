using Ardalis.Specification;
using Backend.Veteriner.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Payments.Specs;

/// <summary>
/// Rapor toplam tutarı için yalnızca <see cref="Payment.Amount"/> projeksiyonu.
/// Filtre kümesi <see cref="PaymentsFilteredCountSpec"/> ile aynı tutulmalıdır.
/// </summary>
public sealed class PaymentsFilteredAmountsSpec : Specification<Payment, decimal>
{
    public PaymentsFilteredAmountsSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid? clientId,
        Guid? petId,
        PaymentMethod? method,
        DateTime paidFromUtc,
        DateTime paidToUtc,
        string? searchContainsLikePattern,
        Guid[] searchMatchClientIds,
        Guid[] searchMatchPetIds)
    {
        Query.AsNoTracking();
        Query.Where(p => p.TenantId == tenantId);
        if (clinicId.HasValue)
            Query.Where(p => p.ClinicId == clinicId.Value);
        if (clientId.HasValue)
            Query.Where(p => p.ClientId == clientId.Value);
        if (petId.HasValue)
            Query.Where(p => p.PetId == petId.Value);
        if (method.HasValue)
            Query.Where(p => p.Method == method.Value);
        Query.Where(p => p.PaidAtUtc >= paidFromUtc && p.PaidAtUtc <= paidToUtc);

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

        Query.Select(p => p.Amount);
    }
}
