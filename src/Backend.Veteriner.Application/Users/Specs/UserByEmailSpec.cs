using Ardalis.Specification;
using Backend.Veteriner.Domain.Users;

namespace Backend.Veteriner.Application.Users.Specs;

public sealed class UserByEmailSpec : Specification<User>, ISingleResultSpecification<User>
{
    public UserByEmailSpec(string email)
    {
        Query.AsNoTracking()
             .Where(u => u.Email == email);
    }
}
