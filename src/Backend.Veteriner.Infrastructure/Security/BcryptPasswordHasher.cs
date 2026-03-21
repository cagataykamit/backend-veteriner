using Backend.Veteriner.Application.Common.Abstractions;

namespace Backend.Veteriner.Infrastructure.Security;

public sealed class BcryptPasswordHasher : IPasswordHasher
{
    // �stersen ileride work factor vs. ayarlar�n� ctor ile alabiliriz.
    public string Hash(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password bo� olamaz.", nameof(password));

        return BCrypt.Net.BCrypt.HashPassword(password);
    }

    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrWhiteSpace(hash)) return false;
        return BCrypt.Net.BCrypt.Verify(password, hash);
    }
}
