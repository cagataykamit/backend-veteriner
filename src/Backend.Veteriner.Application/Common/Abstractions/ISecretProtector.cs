namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// Gizli verinin (entegratör belirteçleri, OAuth state) şifrelenmesi (ADR-036 ilke 6). Altyapıda ASP.NET Data Protection;
/// <c>purpose</c> amaçları birbirinden ayırır (bir amaçla şifrelenen diğeriyle çözülmez).
/// Çözülemeyen veya süresi dolan değer <c>null</c> döner; içerik günlüğe yazılmaz.
/// </summary>
public interface ISecretProtector
{
    string Protect(string purpose, string plaintext);

    string? Unprotect(string purpose, string protectedValue);

    /// <summary>Süreli şifreleme; süre dolunca <see cref="UnprotectTimeLimited"/> <c>null</c> döner.</summary>
    string ProtectTimeLimited(string purpose, string plaintext, TimeSpan lifetime);

    string? UnprotectTimeLimited(string purpose, string protectedValue);
}
