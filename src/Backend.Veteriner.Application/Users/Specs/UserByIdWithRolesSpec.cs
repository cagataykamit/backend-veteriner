using Ardalis.Specification;
using Backend.Veteriner.Domain.Users;

namespace Backend.Veteriner.Application.Users.Specs;

public sealed class UserByIdWithRolesSpec : Specification<User>, ISingleResultSpecification<User>
{
    public UserByIdWithRolesSpec(Guid userId)
    {
        Query
            .Where(u => u.Id == userId)
            .EnableCache(nameof(UserByIdWithRolesSpec), userId); // opsiyonel: Ardalis cache
        // OwnsMany oldu�u i�in EF zaten ayn� sorguda roller tablosuna gider.
    }
}
