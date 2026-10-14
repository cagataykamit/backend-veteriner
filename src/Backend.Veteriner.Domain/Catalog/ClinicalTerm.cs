using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Catalog;

/// <summary>
/// Tanı/şikâyet sözlüğü kaydı (ADR-020 karar 4-5). <c>TenantId</c> null ise sistem varsayılanı (salt okunur, seed ile gelir);
/// doluysa kliniğe özel (eklenir, adı düzeltilir, pasifleştirilir). Muayene kaydına bağlanmaz: seçilen ad muayenenin
/// serbest metin alanına yazılır (arama kaynağı). Tür sonradan değişmez.
/// </summary>
public sealed class ClinicalTerm : AggregateRoot
{
    public const int MaxNameLength = 200;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid? TenantId { get; private set; }
    public ClinicalTermKind Kind { get; private set; }
    public string Name { get; private set; } = default!;

    /// <summary><see cref="TurkishTextKey.Normalize"/> çıktısı; mükerrer kontrolü ve arama bu kolon üzerinden yapılır.</summary>
    public string NormalizedName { get; private set; } = default!;

    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    /// <summary>SQL Server rowversion — eşzamanlı güncelleme için.</summary>
    public byte[] RowVersion { get; private set; } = default!;

    public bool IsSystem => TenantId is null;

    private ClinicalTerm() { }

    private ClinicalTerm(Guid? tenantId, ClinicalTermKind kind, string name)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentException("Sözlük türü geçersiz.", nameof(kind));

        var check = ValidateName(name);
        if (!check.IsSuccess)
            throw new ArgumentException(check.Error.Message, nameof(name));

        TenantId = tenantId;
        Kind = kind;
        ApplyName(name);
        IsActive = true;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public static ClinicalTerm CreateSystem(ClinicalTermKind kind, string name) => new(null, kind, name);

    public static ClinicalTerm CreateForTenant(Guid tenantId, ClinicalTermKind kind, string name)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));

        return new ClinicalTerm(tenantId, kind, name);
    }

    public Result Rename(string name)
    {
        if (IsSystem)
            return SystemReadOnly();

        var check = ValidateName(name);
        if (!check.IsSuccess)
            return check;

        ApplyName(name);
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    public Result Activate()
    {
        if (IsSystem)
            return SystemReadOnly();

        if (!IsActive)
        {
            IsActive = true;
            UpdatedAtUtc = DateTime.UtcNow;
        }

        return Result.Success();
    }

    public Result Deactivate()
    {
        if (IsSystem)
            return SystemReadOnly();

        if (IsActive)
        {
            IsActive = false;
            UpdatedAtUtc = DateTime.UtcNow;
        }

        return Result.Success();
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

    private void ApplyName(string name)
    {
        Name = CollapseWhitespace(name);
        NormalizedName = TurkishTextKey.Normalize(name);
    }

    private static string CollapseWhitespace(string value)
        => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static Result ValidateName(string? name)
    {
        var length = string.IsNullOrWhiteSpace(name) ? 0 : CollapseWhitespace(name).Length;
        return length is >= 1 and <= MaxNameLength
            ? Result.Success()
            : Result.Failure("ClinicalTerms.Validation", $"Ad 1-{MaxNameLength} karakter olmalıdır.");
    }

    private static Result SystemReadOnly()
        => Result.Failure("ClinicalTerms.SystemTermReadOnly", "Sistem sözlük kayıtları değiştirilemez; kliniğe özel kayıt ekleyin.");
}
