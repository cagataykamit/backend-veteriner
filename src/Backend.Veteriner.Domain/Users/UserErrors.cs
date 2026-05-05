using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Users;

public static class UserErrors
{
    public static readonly Error RoleNameEmpty = new("User.RoleNameEmpty", "Role ismi boş olamaz.");
    public static readonly Error RoleAlreadyExists = new("User.RoleAlreadyExists", "Bu role zaten mevcut.");
}
