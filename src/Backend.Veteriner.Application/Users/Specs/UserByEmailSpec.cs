using Ardalis.Specification;
using Backend.Veteriner.Domain.Users;

namespace Backend.Veteriner.Application.Users.Specs;

public sealed class UserByEmailSpec : Specification<User>
{
    public UserByEmailSpec(string email)
    {
        Query.Where(u => u.Email == email)
             .Include(u => u.Roles);
    }
}
