using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Catalog;

/// <summary>
/// Klinik metin parçası (EXAM-011/012): muayene metin alanına <c>#kısayol</c> ile imlecin olduğu yere eklenen kısa hazır metin
/// (ör. <c>#kusma</c> → anamnez soru seti). Kiracı düzeyinde yönetilir (şubeler paylaşır), sistem varsayılanı yoktur.
/// Muayene şablonundan (<see cref="ExaminationTemplate"/>) farkı: sunucuda uygulanmaz, kayıt/işlem satırı üretmez;
/// istemci gövdeyi metne ekler ve muayene kaydıyla birlikte saklanır. Parça sonradan değişse de yazılmış metin değişmez.
/// </summary>
public sealed class ClinicalSnippet : AggregateRoot, ITenantOwned
{
    public const int MinShortcutLength = 2;
    public const int MaxShortcutLength = 30;
    public const int MaxTitleLength = 100;
    public const int MaxBodyLength = 2000;

    /// <summary>Kiracı başına (pasifler dahil) tavan; seçici listesi küçük kalır.</summary>
    public const int MaxPerTenant = 200;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }

    /// <summary>
    /// <c>#</c> sonrası yazılan anahtar: <see cref="TurkishTextKey.Normalize"/> ile küçültülmüş, yalnız <c>a-z 0-9 -</c>
    /// (boşluk yok). Kiracı içinde tek; seçicide bununla süzülür.
    /// </summary>
    public string Shortcut { get; private set; } = default!;

    /// <summary>Seçicide görünen açıklayıcı ad.</summary>
    public string Title { get; private set; } = default!;

    /// <summary><see cref="TurkishTextKey.Normalize"/> çıktısı; liste araması bu kolon üzerinden yapılır.</summary>
    public string NormalizedTitle { get; private set; } = default!;

    /// <summary>Alana eklenecek metin; satır sonları korunur.</summary>
    public string Body { get; private set; } = default!;

    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    /// <summary>SQL Server rowversion — eşzamanlı güncelleme için.</summary>
    public byte[] RowVersion { get; private set; } = default!;

    private ClinicalSnippet() { }

    private ClinicalSnippet(Guid tenantId, string shortcut, string title, string body, DateTime utcNow)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));

        var check = Validate(shortcut, title, body);
        if (!check.IsSuccess)
            throw new ArgumentException(check.Error.Message);

        TenantId = tenantId;
        Apply(shortcut, title, body);
        IsActive = true;
        CreatedAtUtc = utcNow;
    }

    public static Result<ClinicalSnippet> Create(Guid tenantId, string shortcut, string title, string body, DateTime utcNow)
    {
        var check = Validate(shortcut, title, body);
        return check.IsSuccess
            ? Result<ClinicalSnippet>.Success(new ClinicalSnippet(tenantId, shortcut, title, body, utcNow))
            : Result<ClinicalSnippet>.Failure(check.Error);
    }

    public Result Update(string shortcut, string title, string body, DateTime utcNow)
    {
        var check = Validate(shortcut, title, body);
        if (!check.IsSuccess)
            return check;

        Apply(shortcut, title, body);
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    public void Activate(DateTime utcNow)
    {
        if (IsActive)
            return;

        IsActive = true;
        UpdatedAtUtc = utcNow;
    }

    public void Deactivate(DateTime utcNow)
    {
        if (!IsActive)
            return;

        IsActive = false;
        UpdatedAtUtc = utcNow;
    }

    /// <summary>
    /// İstemcinin gördüğü sürüm: kalıcı yazma yalnızca veritabanındaki sürüm bu değere eşitse gerçekleşir.
    /// </summary>
    public void SetExpectedRowVersion(byte[] expectedRowVersion)
    {
        if (expectedRowVersion is null || expectedRowVersion.Length == 0)
            throw new ArgumentException("RowVersion geçersiz.", nameof(expectedRowVersion));

        RowVersion = expectedRowVersion;
    }

    /// <summary>Kısayolun saklanan biçimi: baştaki <c>#</c> atılır, Türkçe harfler katlanır, küçük harfe iner.</summary>
    public static string NormalizeShortcut(string? shortcut)
        => TurkishTextKey.Normalize(shortcut?.Trim().TrimStart('#'));

    /// <summary>Domain ve validator'ın paylaştığı tek kural seti.</summary>
    public static Result Validate(string? shortcut, string? title, string? body)
    {
        var key = NormalizeShortcut(shortcut);
        if (key.Length is < MinShortcutLength or > MaxShortcutLength)
            return Validation($"Kısayol {MinShortcutLength}-{MaxShortcutLength} karakter olmalıdır.");

        if (!key.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
            return Validation("Kısayol yalnız harf, rakam ve tire içerebilir; boşluk olamaz.");

        var titleLength = title?.Trim().Length ?? 0;
        if (titleLength is < 1 or > MaxTitleLength)
            return Validation($"Başlık 1-{MaxTitleLength} karakter olmalıdır.");

        var bodyLength = body?.Trim().Length ?? 0;
        if (bodyLength is < 1 or > MaxBodyLength)
            return Validation($"Metin 1-{MaxBodyLength} karakter olmalıdır.");

        return Result.Success();
    }

    private void Apply(string shortcut, string title, string body)
    {
        Shortcut = NormalizeShortcut(shortcut);
        Title = title.Trim();
        NormalizedTitle = TurkishTextKey.Normalize(title);
        Body = body.Trim().ReplaceLineEndings("\n");
    }

    private static Result Validation(string message) => Result.Failure("ClinicalSnippets.Validation", message);
}
