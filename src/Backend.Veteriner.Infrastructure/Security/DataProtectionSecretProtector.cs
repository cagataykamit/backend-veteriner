using System.Security.Cryptography;
using Backend.Veteriner.Application.Common.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace Backend.Veteriner.Infrastructure.Security;

/// <summary>
/// ASP.NET Data Protection ile gizli veri şifreleme (ADR-036 ilke 6). Anahtar halkası <c>DataProtection:KeysPath</c>
/// dizininde kalıcıdır (canlıda yedeklenmeli; anahtar kaybolursa şifreli belirteçler çözülemez, klinik yeniden bağlanır).
/// </summary>
public sealed class DataProtectionSecretProtector : ISecretProtector
{
    private readonly IDataProtectionProvider _provider;

    public DataProtectionSecretProtector(IDataProtectionProvider provider) => _provider = provider;

    public string Protect(string purpose, string plaintext) => _provider.CreateProtector(purpose).Protect(plaintext);

    public string? Unprotect(string purpose, string protectedValue)
    {
        try
        {
            return _provider.CreateProtector(purpose).Unprotect(protectedValue);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public string ProtectTimeLimited(string purpose, string plaintext, TimeSpan lifetime)
        => _provider.CreateProtector(purpose).ToTimeLimitedDataProtector().Protect(plaintext, lifetime);

    public string? UnprotectTimeLimited(string purpose, string protectedValue)
    {
        try
        {
            return _provider.CreateProtector(purpose).ToTimeLimitedDataProtector().Unprotect(protectedValue);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
