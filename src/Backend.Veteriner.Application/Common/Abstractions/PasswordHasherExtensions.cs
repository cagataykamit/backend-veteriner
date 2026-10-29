namespace Backend.Veteriner.Application.Common.Abstractions;

public static class PasswordHasherExtensions
{
    private static readonly object DummyHashLock = new();
    private static string? _dummyHash;

    /// <summary>
    /// Hesap yok, kilitli veya parolasız olduğunda gerçek doğrulama kadar süren boş doğrulama; yanıt süresinden hesabın
    /// varlığı anlaşılmasın diye (SEC-001). Sonucu her zaman yok sayılır.
    /// </summary>
    public static void VerifyAgainstDummyHash(this IPasswordHasher hasher, string password)
        => hasher.Verify(password, DummyHash(hasher));

    private static string DummyHash(IPasswordHasher hasher)
    {
        if (_dummyHash is not null)
            return _dummyHash;
        lock (DummyHashLock)
            return _dummyHash ??= hasher.Hash(Guid.NewGuid().ToString("N"));
    }
}
