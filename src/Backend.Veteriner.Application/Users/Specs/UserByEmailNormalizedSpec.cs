using Ardalis.Specification;
using Backend.Veteriner.Domain.Users;

namespace Backend.Veteriner.Application.Users.Specs;

/// <summary>E-posta karşılaştırması küçük harf ile (handler tarafında normalize edilmiş değer verin).</summary>
public sealed class UserByEmailNormalizedSpec : Specification<User>
{
    public UserByEmailNormalizedSpec(string normalizedEmailLowerInvariant)
    {
        Query.Where(u => u.Email.ToLower() == normalizedEmailLowerInvariant)
            .Include(u => u.Roles);
    }
}
