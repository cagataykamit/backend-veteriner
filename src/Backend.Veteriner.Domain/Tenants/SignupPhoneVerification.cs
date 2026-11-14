using Backend.Veteriner.Domain.Clients;

namespace Backend.Veteriner.Domain.Tenants;

/// <summary>
/// Deneme kaydının telefon doğrulaması (TRIAL-001, INT-003): kayıt, cep telefonuna gelen tek kullanımlık kodla doğrulanmadan
/// açılmaz ve bir numarayla yalnız bir deneme hesabı açılır. Düz kod ve düz jeton saklanmaz; özetler satır kimliğiyle tuzlanır
/// (uygulama katmanı hesaplar). Kod, süre, deneme hakkı, bekleme ve günlük sınır kuralları yalnız burada yaşar.
/// </summary>
/// <remarks>
/// Akış: kod üretilir (<see cref="Issue"/>) → kod doğrulanır, kısa ömürlü kayıt jetonu verilir (<see cref="TryVerify"/>) → kayıt
/// jetonla aynı işlemde satırı kullanır (<see cref="MarkUsed"/>). Kullanılmış satır numaranın deneme hakkını kalıcı olarak kapatır
/// (veritabanında numara başına tek kullanılmış satır).
/// </remarks>
public sealed class SignupPhoneVerification
{
    public const int CodeLength = 6;
    public const int MaxFailedAttempts = 5;
    public const int HashMaxLength = 64;

    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);

    /// <summary>Aynı numaraya bu süre dolmadan yeni kod gönderilmez.</summary>
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);

    /// <summary>Doğrulanan numarayla kaydı tamamlama süresi (form doldurma payı).</summary>
    public static readonly TimeSpan SignupTokenLifetime = TimeSpan.FromMinutes(30);

    /// <summary>Günlük sınırların penceresi (kayan 24 saat).</summary>
    public static readonly TimeSpan DailyWindow = TimeSpan.FromHours(24);

    public const int MaxCodesPerPhonePerDay = 5;
    public const int MaxCodesPerIpPerDay = 10;

    /// <summary>Tüm numaralar için günlük üst sınır: dağıtık kötüye kullanımda SMS bütçesini korur.</summary>
    public const int MaxCodesPerDay = 1000;

    public Guid Id { get; private set; }

    /// <summary><see cref="TurkishMobilePhone"/> biçiminde (905XXXXXXXXX).</summary>
    public string Phone { get; private set; } = default!;

    public string CodeHash { get; private set; } = default!;

    /// <summary>İsteğin IP adresinin özeti (IP başına günlük sınır için; düz IP saklanmaz). IP bilinmiyorsa boş.</summary>
    public string? RequestIpHash { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public int FailedAttempts { get; private set; }
    public DateTime? VerifiedAtUtc { get; private set; }
    public string? SignupTokenHash { get; private set; }
    public DateTime? SignupTokenExpiresAtUtc { get; private set; }
    public DateTime? UsedAtUtc { get; private set; }
    public Guid? UsedByTenantId { get; private set; }
    public DateTime? InvalidatedAtUtc { get; private set; }

    private SignupPhoneVerification() { }

    /// <param name="id">Özet tuzu olarak kullanıldığı için özetten önce üretilir.</param>
    /// <param name="normalizedPhone"><see cref="TurkishMobilePhone.TryNormalize"/> çıktısı.</param>
    public static SignupPhoneVerification Issue(Guid id, string normalizedPhone, string codeHash, string? requestIpHash, DateTime utcNow)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Kimlik geçersiz.", nameof(id));
        if (!TurkishMobilePhone.TryNormalize(normalizedPhone, out var phone) || phone != normalizedPhone)
            throw new ArgumentException("Telefon 905XXXXXXXXX biçiminde olmalıdır.", nameof(normalizedPhone));
        if (string.IsNullOrWhiteSpace(codeHash))
            throw new ArgumentException("Kod özeti boş olamaz.", nameof(codeHash));

        return new SignupPhoneVerification
        {
            Id = id,
            Phone = normalizedPhone,
            CodeHash = codeHash,
            RequestIpHash = string.IsNullOrWhiteSpace(requestIpHash) ? null : requestIpHash,
            CreatedAtUtc = utcNow,
            ExpiresAtUtc = utcNow.Add(CodeLifetime),
        };
    }

    /// <summary>Kod doğrulanabilir: doğrulanmamış, geçersiz kılınmamış, süresi ve deneme hakkı bitmemiş.</summary>
    public bool IsCodeActive(DateTime utcNow) =>
        VerifiedAtUtc is null && InvalidatedAtUtc is null && FailedAttempts < MaxFailedAttempts && utcNow < ExpiresAtUtc;

    public bool IsInResendCooldown(DateTime utcNow) => utcNow - CreatedAtUtc < ResendCooldown;

    /// <summary>Yeni kod istenmeden önce kalan bekleme (bekleme yoksa sıfır).</summary>
    public TimeSpan ResendAvailableIn(DateTime utcNow)
    {
        var remaining = CreatedAtUtc.Add(ResendCooldown) - utcNow;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    /// <summary>Kullanılmamış satırı kapatır (aynı numaraya yeni kod gönderilince önceki kod ve jeton geçersiz olur).</summary>
    public void Invalidate(DateTime utcNow)
    {
        if (UsedAtUtc is null)
            InvalidatedAtUtc ??= utcNow;
    }

    /// <summary>
    /// Kod denemesi. Eşleşmezse deneme sayılır, hak bitince kod geçersiz olur. Eşleşirse numara doğrulanır ve kayıt jetonu
    /// (<paramref name="signupTokenHash"/>) <see cref="SignupTokenLifetime"/> boyunca geçerli olur. Aktif olmayan kodda her zaman false.
    /// </summary>
    public bool TryVerify(bool codeMatches, string signupTokenHash, DateTime utcNow)
    {
        if (!IsCodeActive(utcNow))
            return false;

        if (!codeMatches)
        {
            FailedAttempts++;
            if (FailedAttempts >= MaxFailedAttempts)
                InvalidatedAtUtc = utcNow;
            return false;
        }

        if (string.IsNullOrWhiteSpace(signupTokenHash))
            throw new ArgumentException("Jeton özeti boş olamaz.", nameof(signupTokenHash));

        VerifiedAtUtc = utcNow;
        SignupTokenHash = signupTokenHash;
        SignupTokenExpiresAtUtc = utcNow.Add(SignupTokenLifetime);
        return true;
    }

    /// <summary>Kayıtta kullanılabilir: doğrulanmış, kullanılmamış, geçersiz kılınmamış ve jeton süresi dolmamış.</summary>
    public bool CanBeUsedForSignup(DateTime utcNow) =>
        VerifiedAtUtc is not null && UsedAtUtc is null && InvalidatedAtUtc is null
        && SignupTokenExpiresAtUtc is { } expiresAt && utcNow < expiresAt;

    /// <summary>Kayıt aynı işlemde satırı kullanır; numara artık yeni deneme açamaz.</summary>
    public void MarkUsed(Guid tenantId, DateTime utcNow)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Kiracı kimliği geçersiz.", nameof(tenantId));
        if (!CanBeUsedForSignup(utcNow))
            throw new InvalidOperationException("Doğrulama kayıtta kullanılamaz.");

        UsedAtUtc = utcNow;
        UsedByTenantId = tenantId;
    }
}
