using Ardalis.Specification;
using Backend.Veteriner.Domain.Clients;

namespace Backend.Veteriner.Application.Clients.Specs;

/// <summary>
/// Aynı kiracıda saklanmış normalize e-posta + normalize telefon (rakamlar) çifti; mükerrer oluşturmayı engellemek için.
/// </summary>
public sealed class ClientByTenantNormalizedEmailAndPhoneSpec : Specification<Client>
{
    public ClientByTenantNormalizedEmailAndPhoneSpec(Guid tenantId, string normalizedEmail, string normalizedPhone)
    {
        Query.Where(c =>
            c.TenantId == tenantId
            && c.Email == normalizedEmail
            && c.PhoneNormalized == normalizedPhone);
    }
}
