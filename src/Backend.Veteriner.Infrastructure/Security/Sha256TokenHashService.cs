using Backend.Veteriner.Application.Common.Abstractions;
using System.Security.Cryptography;
using System.Text;

public sealed class Sha256TokenHashService : ITokenHashService
{
    public string ComputeSha256(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            throw new ArgumentException("Token bo� olamaz.", nameof(rawToken));

        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(rawToken));

        // lowercase hex (64 char)
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
