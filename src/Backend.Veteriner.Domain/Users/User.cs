using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Users;

/// <summary>
/// User aggregate root.
/// Kimlik, rol ve refresh token ya�am d�ng�s�n� y�netir.
/// </summary>
public class User : AggregateRoot
{
    private readonly List<UserRole> _roles = new();
    private readonly List<RefreshToken> _refreshTokens = new();

    public Guid Id { get; private set; } = Guid.NewGuid();

    public string Email { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;

    public bool EmailConfirmed { get; private set; }

    /// <summary>Kullanicinin gorunen adi (opsiyonel). Bos ise e-postadan turetilen ad kullanilir.</summary>
    public string? DisplayName { get; private set; }

    public const int MaxDisplayNameLength = 120;

    /// <summary>
    /// Kullan?c?n?n olu�turulma zaman? (UTC).
    /// Admin listeleme, audit ve raporlama i�in kritik metadatad?r.
    /// </summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>
    /// Kullan?c?n?n son g�ncellenme zaman? (UTC). Opsiyonel ama �nerilir.
    /// </summary>
    public DateTime? UpdatedAtUtc { get; private set; }

    /// <summary>Kilitten bu yana art arda başarısız giriş sayısı (SEC-001).</summary>
    public int FailedLoginCount { get; private set; }

    public DateTime? LockedUntilUtc { get; private set; }

    public IReadOnlyCollection<UserRole> Roles => _roles.AsReadOnly();
    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();

    private User() { } // EF i�in

    public User(string email, string passwordHash, DateTime utcNow)
    {
        Email = email ?? throw new ArgumentNullException(nameof(email));
        PasswordHash = passwordHash ?? throw new ArgumentNullException(nameof(passwordHash));

        // Kurumsal standart: olu�turma an?n? domain'de sabitle
        CreatedAtUtc = utcNow;
        UpdatedAtUtc = null;
    }

    /// <summary>Gorunen adi ayarlar; bos/bosluk adi temizler. Uzunluk siniri asilirsa Result.Failure.</summary>
    public Result SetDisplayName(string? displayName, DateTime utcNow)
    {
        var normalized = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        if (normalized is { Length: > MaxDisplayNameLength })
            return Result.Failure("Users.Validation", $"Gorunen ad en fazla {MaxDisplayNameLength} karakter olabilir.");

        DisplayName = normalized;
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    public void ConfirmEmail(DateTime utcNow)
    {
        EmailConfirmed = true;
        UpdatedAtUtc = utcNow;
    }

    /// <summary>
    /// Role ekler. Domain kural ihlallerinde exception f?rlatmaz, Result d�ner.
    /// </summary>
    public Result AddRole(string roleName, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(roleName))
            return Result.Failure(UserErrors.RoleNameEmpty);

        var normalized = roleName.Trim();

        if (_roles.Any(r => string.Equals(r.Name, normalized, StringComparison.OrdinalIgnoreCase)))
            return Result.Failure(UserErrors.RoleAlreadyExists);

        _roles.Add(new UserRole(normalized));
        UpdatedAtUtc = utcNow;

        return Result.Success();
    }

    public void AddRefreshToken(RefreshToken token, DateTime utcNow)
    {
        _refreshTokens.Add(token);
        UpdatedAtUtc = utcNow;
    }

    public void RevokeRefreshToken(string tokenHash, DateTime utcNow)
    {
        var token = _refreshTokens.FirstOrDefault(t => t.TokenHash == tokenHash);
        token?.Revoke(utcNow);
        UpdatedAtUtc = utcNow;
    }

    /// <summary>Yeni parolayla eski tahminler geçersizleşir; kilit ve sayaç da temizlenir.</summary>
    public void UpdatePasswordHash(string newHash, DateTime utcNow)
    {
        PasswordHash = newHash ?? throw new ArgumentNullException(nameof(newHash));
        UpdatedAtUtc = utcNow;
        ClearLockout();
    }

    public bool IsLockedOut(DateTime utcNow) => SignInLockout.IsLockedOut(LockedUntilUtc, utcNow);

    /// <summary>Başarısız giriş; kural <see cref="SignInLockout.RecordFailure"/>. Hesap bu denemeyle kilitlendiyse true.</summary>
    public bool RecordFailedLogin(DateTime utcNow)
    {
        var wasLockedOut = IsLockedOut(utcNow);
        (FailedLoginCount, LockedUntilUtc) = SignInLockout.RecordFailure(FailedLoginCount, LockedUntilUtc, utcNow);
        return !wasLockedOut && IsLockedOut(utcNow);
    }

    /// <summary>Başarılı giriş veya parola değişikliği: sayaç ve kilit temizlenir.</summary>
    public void ClearLockout()
    {
        FailedLoginCount = 0;
        LockedUntilUtc = null;
    }
}
