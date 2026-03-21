namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// Parola hashleme ve do�rulama i�lemleri i�in s�zle�me.
/// Modern olarak bcrypt (veya Argon2) implementasyonlar� kullan�l�r.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// D�z parolay� g�venli bi�imde hashler.
    /// </summary>
    string Hash(string password);

    /// <summary>
    /// Kullan�c�n�n girdi�i parolay� mevcut hash ile do�rular.
    /// </summary>
    bool Verify(string password, string hash);
}
