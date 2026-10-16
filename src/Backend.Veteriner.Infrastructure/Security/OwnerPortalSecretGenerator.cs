using System.Security.Cryptography;
using Backend.Veteriner.Application.Common.Abstractions;

namespace Backend.Veteriner.Infrastructure.Security;

/// <summary>Portal jeton ve kodları için kriptografik rastgele üretim.</summary>
public sealed class OwnerPortalSecretGenerator : IOwnerPortalSecretGenerator
{
    public string CreateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public string CreateNumericCode(int length)
    {
        if (length <= 0)
            throw new ArgumentOutOfRangeException(nameof(length));

        var digits = new char[length];
        for (var i = 0; i < length; i++)
            digits[i] = (char)('0' + RandomNumberGenerator.GetInt32(10));
        return new string(digits);
    }
}
