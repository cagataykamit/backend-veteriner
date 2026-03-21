using Ardalis.Specification;
using Backend.Veteriner.Domain.Clients;

namespace Backend.Veteriner.Application.Clients.Specs;

/// <summary>
/// Aynı kiracı altında aynı kişi kaydını tekrarlamayı engellemek için:
/// tam ad (case-insensitive) + telefon birlikte eşleşirse duplicate sayılır.
/// Telefon yoksa <paramref name="phoneNormalized"/> boş string olmalıdır; DB tarafında <c>Phone</c> null ise <c>""</c> ile karşılaştırılır.
/// </summary>
public sealed class ClientByTenantFullNameAndPhoneSpec : Specification<Client>
{
    public ClientByTenantFullNameAndPhoneSpec(Guid tenantId, string fullNameLowerInvariant, string phoneNormalized)
    {
        Query.Where(c =>
            c.TenantId == tenantId
            && c.FullName.ToLower() == fullNameLowerInvariant
            && (c.Phone ?? "") == phoneNormalized);
    }
}
