using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Specifications;
using Backend.Veteriner.Domain.ClientCredits;

namespace Backend.Veteriner.Application.ClientCredits;

/// <summary>Müşterinin bir klinikteki açık kredileri (mahsup için izlenir); FIFO sırası domain'de verilir.</summary>
public sealed class OpenClientCreditsForUpdateSpec : Specification<ClientCredit>
{
    public OpenClientCreditsForUpdateSpec(Guid tenantId, Guid clientId, Guid clinicId)
    {
        Query.Where(c => c.TenantId == tenantId && c.ClientId == clientId && c.ClinicId == clinicId
                         && c.RefundedAtUtc == null && c.VoidedAtUtc == null && c.UsedAmount < c.Amount);
        Query.OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.Id);
    }
}

/// <summary>Bakiye ve liste: müşterinin kapsamdaki kredileri; <paramref name="openOnly"/> ise yalnız kalanı olanlar.</summary>
public sealed class ClientCreditsForClientSpec : Specification<ClientCredit>
{
    public ClientCreditsForClientSpec(
        Guid tenantId,
        Guid clientId,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds,
        bool openOnly)
    {
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && c.ClientId == clientId);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        if (openOnly)
            Query.Where(c => c.RefundedAtUtc == null && c.VoidedAtUtc == null && c.UsedAmount < c.Amount);
        Query.OrderByDescending(c => c.CreatedAtUtc).ThenBy(c => c.Id);
    }
}

public sealed class ClientCreditByIdForUpdateSpec : Specification<ClientCredit>
{
    public ClientCreditByIdForUpdateSpec(Guid tenantId, Guid clientId, Guid id)
        => Query.Where(c => c.TenantId == tenantId && c.ClientId == clientId && c.Id == id);
}

/// <summary>Ödemenin kredisi (ödeme iptali için izlenir).</summary>
public sealed class ClientCreditBySourcePaymentForUpdateSpec : Specification<ClientCredit>
{
    public ClientCreditBySourcePaymentForUpdateSpec(Guid tenantId, Guid paymentId)
        => Query.Where(c => c.TenantId == tenantId && c.SourcePaymentId == paymentId);
}

public sealed class ClientCreditApplicationByIdForUpdateSpec : Specification<ClientCreditApplication>
{
    public ClientCreditApplicationByIdForUpdateSpec(Guid tenantId, Guid id)
        => Query.Where(a => a.TenantId == tenantId && a.Id == id);
}

public sealed class ClientCreditApplicationsByCreditIdsSpec : Specification<ClientCreditApplication>
{
    public ClientCreditApplicationsByCreditIdsSpec(Guid tenantId, IReadOnlyCollection<Guid> creditIds)
    {
        Query.AsNoTracking();
        Query.Where(a => a.TenantId == tenantId && creditIds.Contains(a.CreditId));
        Query.OrderBy(a => a.CreatedAtUtc).ThenBy(a => a.Id);
    }
}

/// <summary>Ücretin aktif (geri alınmamış) mahsupları.</summary>
public sealed class ActiveClientCreditApplicationsByChargeSpec : Specification<ClientCreditApplication>
{
    public ActiveClientCreditApplicationsByChargeSpec(Guid tenantId, Guid chargeId)
    {
        Query.AsNoTracking();
        Query.Where(a => a.TenantId == tenantId && a.ChargeId == chargeId && a.ReversedAtUtc == null);
        Query.OrderBy(a => a.CreatedAtUtc).ThenBy(a => a.Id);
    }
}

/// <summary>Aynı istek anahtarıyla yazılmış mahsuplar (tekrar isteği).</summary>
public sealed class ClientCreditApplicationsByRequestIdSpec : Specification<ClientCreditApplication>
{
    public ClientCreditApplicationsByRequestIdSpec(Guid tenantId, Guid requestId)
    {
        Query.AsNoTracking();
        Query.Where(a => a.TenantId == tenantId && a.RequestId == requestId);
        Query.OrderBy(a => a.CreatedAtUtc).ThenBy(a => a.Id);
    }
}

public sealed class ClientCreditsByIdsSpec : Specification<ClientCredit>
{
    public ClientCreditsByIdsSpec(Guid tenantId, IReadOnlyCollection<Guid> ids)
    {
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && ids.Contains(c.Id));
    }
}
