using System.Security.Cryptography;
using System.Text;
using Backend.Veteriner.Domain.ExternalApi;

namespace Backend.Veteriner.Application.ExternalApi;

/// <summary>
/// Dış API anahtarının biçimi, üretimi ve doğrulaması — tek yer (TIER-009; docs/EXTERNAL_API_CONTRACT.md §2). Biçim
/// <c>vtk_</c> + 8 karakter ön ek + <c>_</c> + 32 rastgele baytın base64url hâli (43 karakter). Saklanan, anahtarın tamamının SHA-256
/// özetidir; anahtar 256 bit rastgele olduğundan yavaş parola özeti gerekmez. Karşılaştırma sabit zamanlıdır.
/// </summary>
public static class ApiKeySecret
{
    public const string Marker = "vtk_";
    private const int PrefixRandomLength = 8;
    private const int SecretBytes = 32;
    private const int SecretLength = 43;
    public const int KeyLength = ApiKey.PrefixLength + 1 + SecretLength;

    private const string PrefixAlphabet = "abcdefghijkmnpqrstuvwxyz23456789";

    /// <summary>Ön ek bulunamadığında da aynı sürede karşılaştırma yapılsın diye kullanılan özet.</summary>
    private static readonly byte[] MissingKeyHash = new byte[ApiKey.SecretHashLength];

    public sealed record Generated(string PlainText, string Prefix, byte[] Hash);

    public static Generated Generate()
    {
        var prefix = Marker + RandomNumberGenerator.GetString(PrefixAlphabet, PrefixRandomLength);
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var plainText = $"{prefix}_{secret}";
        return new Generated(plainText, prefix, Hash(plainText));
    }

    /// <summary>Biçim uygunsa ön eki verir; özet doğrulaması ayrıca yapılır.</summary>
    public static bool TryGetPrefix(string? presented, out string prefix)
    {
        prefix = string.Empty;
        if (presented is null || presented.Length != KeyLength || !presented.StartsWith(Marker, StringComparison.Ordinal)
            || presented[ApiKey.PrefixLength] != '_')
            return false;

        for (var i = Marker.Length; i < ApiKey.PrefixLength; i++)
        {
            if (!PrefixAlphabet.Contains(presented[i]))
                return false;
        }

        for (var i = ApiKey.PrefixLength + 1; i < presented.Length; i++)
        {
            var c = presented[i];
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
                return false;
        }

        prefix = presented[..ApiKey.PrefixLength];
        return true;
    }

    public static byte[] Hash(string plainText) => SHA256.HashData(Encoding.UTF8.GetBytes(plainText));

    /// <summary>Sunulan anahtarın özeti saklanan özetle eşit mi; saklanan yoksa da aynı karşılaştırma yapılır, sonuç <c>false</c>.</summary>
    public static bool Verify(string presented, byte[]? storedHash)
    {
        var equal = CryptographicOperations.FixedTimeEquals(Hash(presented), storedHash ?? MissingKeyHash);
        return storedHash is not null && equal;
    }
}
