using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.OwnerPortal;

/// <summary>
/// Hasta sahibi portal hesabı (ADR-031 K3, AK-3): kişi başına tek hesap, klinik/müşteri kayıtlarına
/// <see cref="OwnerAccountClientLink"/> ile bağlanır. Personel kullanıcısından (<c>User</c>) ayrı bir kimliktir.
/// Parola opsiyoneldir; hesap e-posta koduyla da girer (AK-2). Kilit kuralı yalnızca burada yaşar.
/// </summary>
public sealed class OwnerAccount
{
    public const int MaxEmailLength = 320;
    public const int MaxDisplayNameLength = 100;

    /// <summary>Silinen hesabın görünen adı (ADR-041 K3).</summary>
    public const string DeletedDisplayName = "Silinmiş hesap";

    /// <summary>Art arda bu kadar başarısız giriş (yanlış parola veya kod) hesabı kilitler.</summary>
    public const int MaxFailedSignInAttempts = 5;

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public Guid Id { get; private set; } = Guid.NewGuid();

    /// <summary>Trim + küçük harf; benzersiz. Yalnız silinen hesapta null (adres yeni davete serbest kalır).</summary>
    public string? Email { get; private set; }

    /// <summary>BCrypt özeti; yalnız kodla giren hesapta null.</summary>
    public string? PasswordHash { get; private set; }

    public string DisplayName { get; private set; } = default!;
    public OwnerAccountStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? LastLoginAtUtc { get; private set; }

    /// <summary>Kilitten bu yana art arda başarısız giriş sayısı.</summary>
    public int FailedSignInCount { get; private set; }

    public DateTime? LockedUntilUtc { get; private set; }

    public DateTime? DeletedAtUtc { get; private set; }

    private OwnerAccount() { }

    public static OwnerAccount Create(string email, string displayName, DateTime utcNow)
    {
        var normalized = NormalizeEmail(email);
        if (normalized is null)
            throw new ArgumentException("E-posta boş olamaz.", nameof(email));
        if (normalized.Length > MaxEmailLength)
            throw new ArgumentException($"E-posta en fazla {MaxEmailLength} karakter olabilir.", nameof(email));

        return new OwnerAccount
        {
            Email = normalized,
            DisplayName = NormalizeDisplayName(displayName),
            Status = OwnerAccountStatus.Active,
            CreatedAtUtc = ToUtc(utcNow),
        };
    }

    public bool HasPassword => PasswordHash is not null;

    public bool IsActive => Status == OwnerAccountStatus.Active;

    public bool IsLockedOut(DateTime utcNow) => LockedUntilUtc is { } until && ToUtc(utcNow) < until;

    /// <summary>Kimlik bilgisi (parola/kod) denenebilir mi: hesap aktif ve kilitli değil.</summary>
    public bool CanAttemptSignIn(DateTime utcNow) => IsActive && !IsLockedOut(utcNow);

    public void SetPasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("Parola özeti boş olamaz.", nameof(passwordHash));
        PasswordHash = passwordHash;
    }

    /// <summary>
    /// Başarısız giriş: sayaç artar, <see cref="MaxFailedSignInAttempts"/>'a ulaşınca hesap <see cref="LockoutDuration"/>
    /// kilitlenir ve sayaç sıfırlanır. Kilitliyken çağrı kilidi uzatmaz.
    /// </summary>
    public void RecordFailedSignIn(DateTime utcNow)
    {
        var now = ToUtc(utcNow);
        if (IsLockedOut(now))
            return;

        FailedSignInCount++;
        if (FailedSignInCount >= MaxFailedSignInAttempts)
        {
            LockedUntilUtc = now.Add(LockoutDuration);
            FailedSignInCount = 0;
        }
    }

    public void RecordSuccessfulSignIn(DateTime utcNow)
    {
        FailedSignInCount = 0;
        LockedUntilUtc = null;
        LastLoginAtUtc = ToUtc(utcNow);
    }

    /// <summary>Kodla doğrulanmış parola sıfırlamada kilit ve sayaç temizlenir.</summary>
    public void ClearLockout()
    {
        FailedSignInCount = 0;
        LockedUntilUtc = null;
    }

    public void Disable() => Status = OwnerAccountStatus.Disabled;

    /// <summary>
    /// Hasta sahibinin kendi isteğiyle silme (ADR-041 seçenek C): kişisel alanlar geri döndürülemez biçimde boşaltılır, satır
    /// kimliksiz iz olarak kalır (<see cref="Id"/>, <see cref="CreatedAtUtc"/>). Silinen hesap pasiftir; oturum, kod ve bağ
    /// kapatma çağıranın işidir.
    /// </summary>
    public Result Delete(DateTime utcNow)
    {
        if (Status == OwnerAccountStatus.Deleted)
            return Result.Failure("Portal.Account.AlreadyDeleted", "Portal hesabı zaten silinmiş.");

        Email = null;
        PasswordHash = null;
        DisplayName = DeletedDisplayName;
        LastLoginAtUtc = null;
        FailedSignInCount = 0;
        LockedUntilUtc = null;
        Status = OwnerAccountStatus.Deleted;
        DeletedAtUtc = ToUtc(utcNow);
        return Result.Success();
    }

    /// <summary>Hesap e-postası karşılaştırma ve saklama kuralı (trim + küçük harf); boşsa null.</summary>
    public static string? NormalizeEmail(string? email)
        => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    private static string NormalizeDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Görünen ad boş olamaz.", nameof(displayName));
        var trimmed = displayName.Trim();
        return trimmed.Length <= MaxDisplayNameLength ? trimmed : trimmed[..MaxDisplayNameLength];
    }

    private static DateTime ToUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
