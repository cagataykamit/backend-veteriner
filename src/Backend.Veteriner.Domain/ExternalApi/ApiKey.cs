using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.ExternalApi;

/// <summary>
/// Kiracıya özel dış API anahtarı (TIER-009; docs/EXTERNAL_API_CONTRACT.md). Anahtarın kendisi saklanmaz: yalnız SHA-256 özeti ve
/// panelde tanımak için kısa ön eki tutulur; açık metin yalnız oluşturulduğu yanıtta bir kez döner. İptal geri alınmaz, kayıt silinmez.
/// </summary>
public sealed class ApiKey : ITenantOwned
{
    public const int MaxNameLength = 100;

    /// <summary>Ön ek: <c>vtk_</c> + 8 karakter; tüm kiracılarda benzersiz, anahtarın gizli kısmı değildir.</summary>
    public const int PrefixLength = 12;

    public const int SecretHashLength = 32;
    public const int MaxScopesLength = 200;

    /// <summary>Kiracı başına en çok etkin (iptal edilmemiş) anahtar.</summary>
    public const int MaxActivePerTenant = 10;

    /// <summary>Son kullanım her istekte değil, bu aralıktan eskiyse yazılır.</summary>
    public static readonly TimeSpan LastUsedPrecision = TimeSpan.FromMinutes(1);

    public const string ValidationCode = "ApiKeys.Validation";

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = default!;
    public string Prefix { get; private set; } = default!;
    public byte[] SecretHash { get; private set; } = default!;

    /// <summary><see cref="ApiKeyScopes.Separator"/> ile ayrılmış, sıralı kapsamlar.</summary>
    public string Scopes { get; private set; } = default!;

    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? LastUsedAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public Guid? RevokedByUserId { get; private set; }
    public bool IsRevoked => RevokedAtUtc.HasValue;

    private ApiKey() { }

    public static Result<ApiKey> Create(
        Guid tenantId,
        string? name,
        IEnumerable<string>? scopes,
        string prefix,
        byte[] secretHash,
        Guid createdByUserId,
        DateTime nowUtc)
    {
        if (tenantId == Guid.Empty)
            return Invalid("TenantId geçersiz.");
        if (createdByUserId == Guid.Empty)
            return Invalid("Oluşturan kullanıcı geçersiz.");

        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return Invalid("Anahtar adı zorunludur.");
        if (trimmed.Length > MaxNameLength)
            return Invalid($"Anahtar adı en fazla {MaxNameLength} karakter olabilir.");

        var requested = (scopes ?? []).Select(s => s?.Trim() ?? string.Empty).Distinct(StringComparer.Ordinal).ToList();
        if (requested.Count == 0)
            return Invalid("En az bir kapsam seçilmelidir.");
        if (requested.FirstOrDefault(s => !ApiKeyScopes.All.Contains(s, StringComparer.Ordinal)) is { } unknown)
            return Invalid($"Bilinmeyen kapsam: '{unknown}'.");

        if (prefix is null || prefix.Length != PrefixLength)
            return Invalid("Anahtar ön eki geçersiz.");
        if (secretHash is null || secretHash.Length != SecretHashLength)
            return Invalid("Anahtar özeti geçersiz.");

        return Result<ApiKey>.Success(new ApiKey
        {
            TenantId = tenantId,
            Name = trimmed,
            Prefix = prefix,
            SecretHash = secretHash,
            Scopes = string.Join(ApiKeyScopes.Separator, requested.Order(StringComparer.Ordinal)),
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = NormalizeUtc(nowUtc),
        });
    }

    public IReadOnlyList<string> ScopeList => Scopes.Split(ApiKeyScopes.Separator, StringSplitOptions.RemoveEmptyEntries);

    public Result Revoke(Guid revokedByUserId, DateTime nowUtc)
    {
        if (IsRevoked)
            return Result.Failure("ApiKeys.AlreadyRevoked", "Anahtar zaten iptal edilmiş.");
        if (revokedByUserId == Guid.Empty)
            return Result.Failure(ValidationCode, "İptal eden kullanıcı geçersiz.");

        RevokedAtUtc = NormalizeUtc(nowUtc);
        RevokedByUserId = revokedByUserId;
        return Result.Success();
    }

    /// <summary>Son kullanımı günceller; değişiklik yoksa (aralık dolmadıysa) <c>false</c>, kayıt gerekmez.</summary>
    public bool MarkUsed(DateTime nowUtc)
    {
        var now = NormalizeUtc(nowUtc);
        if (LastUsedAtUtc is { } last && now - last < LastUsedPrecision)
            return false;

        LastUsedAtUtc = now;
        return true;
    }

    private static Result<ApiKey> Invalid(string message) => Result<ApiKey>.Failure(ValidationCode, message);

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
