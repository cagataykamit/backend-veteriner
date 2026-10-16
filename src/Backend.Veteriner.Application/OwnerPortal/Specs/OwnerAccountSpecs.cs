using Ardalis.Specification;
using Backend.Veteriner.Domain.Legal;
using Backend.Veteriner.Domain.OwnerPortal;

namespace Backend.Veteriner.Application.OwnerPortal.Specs;

/// <summary>Normalize edilmiş e-postayla hesap (izlenir; giriş denemesi sayaçları güncellenir).</summary>
public sealed class OwnerAccountByEmailSpec : Specification<OwnerAccount>
{
    public OwnerAccountByEmailSpec(string normalizedEmail)
    {
        Query.Where(x => x.Email == normalizedEmail);
    }
}

public sealed class OwnerAccountByIdSpec : Specification<OwnerAccount>
{
    public OwnerAccountByIdSpec(Guid id)
    {
        Query.Where(x => x.Id == id);
    }
}

public sealed class OwnerAccountsByIdsSpec : Specification<OwnerAccount>
{
    public OwnerAccountsByIdsSpec(IReadOnlyCollection<Guid> ids)
    {
        Query.AsNoTracking();
        Query.Where(x => ids.Contains(x.Id));
    }
}

/// <summary>Portal hesabının hukuki belge kabulleri (ADR-029 genişletmesi).</summary>
public sealed class LegalAcceptancesByOwnerAccountSpec : Specification<LegalAcceptance>
{
    public LegalAcceptancesByOwnerAccountSpec(Guid ownerAccountId)
    {
        Query.Where(x => x.OwnerAccountId == ownerAccountId).AsNoTracking();
    }
}
