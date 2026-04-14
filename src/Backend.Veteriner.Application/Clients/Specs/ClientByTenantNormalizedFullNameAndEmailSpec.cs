using Ardalis.Specification;
using Backend.Veteriner.Domain.Clients;

namespace Backend.Veteriner.Application.Clients.Specs;

/// <summary>
/// Aynı kiracıda normalize ad (trim + lower) + saklanmış normalize e-posta ile mükerrer (create/update).
/// </summary>
public sealed class ClientByTenantNormalizedFullNameAndEmailSpec : Specification<Client>
{
    public ClientByTenantNormalizedFullNameAndEmailSpec(
        Guid tenantId,
        string normalizedFullNameLower,
        string normalizedEmail)
    {
        Query.Where(c =>
            c.TenantId == tenantId
            && c.Email == normalizedEmail
            && c.FullName.Trim().ToLower() == normalizedFullNameLower);
    }
}
