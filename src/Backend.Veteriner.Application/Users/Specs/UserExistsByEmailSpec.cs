using Ardalis.Specification;
using Backend.Veteriner.Domain.Users;

namespace Backend.Veteriner.Application.Users.Specs;

/// <summary>
/// Sadece varlık kontrolü için hafif e-posta filtresi (role include/materialization yok).
/// </summary>
public sealed class UserExistsByEmailSpec : Specification<User>
{
    public UserExistsByEmailSpec(string email)
    {
        Query.AsNoTracking()
             .Where(u => u.Email == email);
    }
}
