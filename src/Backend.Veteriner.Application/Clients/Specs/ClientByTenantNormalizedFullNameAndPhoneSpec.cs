using Ardalis.Specification;
using Backend.Veteriner.Domain.Clients;

namespace Backend.Veteriner.Application.Clients.Specs;

/// <summary>
/// Aynı kiracıda normalize ad (Türkçe duyarlı, aksan duyarsız anahtar) + normalize telefon (905…) ile mükerrer (create/update).
/// </summary>
public sealed class ClientByTenantNormalizedFullNameAndPhoneSpec : Specification<Client>
{
    public ClientByTenantNormalizedFullNameAndPhoneSpec(
        Guid tenantId,
        string normalizedFullNameLower,
        string normalizedPhone)
    {
        Query.Where(c =>
            c.TenantId == tenantId
            && c.PhoneNormalized == normalizedPhone);
        Query.Where(ClientFullNameDuplicateKey.Matches(normalizedFullNameLower));
    }
}
