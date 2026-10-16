namespace Backend.Veteriner.Domain.Legal;

/// <summary>
/// Bir kullanıcının bir hukuki belge sürümünü kabul ettiği an (ADR-029). Değiştirilmez ve silinmez;
/// her kabul yeni satırdır. Veri azaltma gereği IP/cihaz bilgisi tutulmaz.
/// Kabul eden ya personel kullanıcısıdır (<see cref="UserId"/> + kiracı) ya da hasta sahibi portal hesabıdır
/// (<see cref="OwnerAccountId"/>; hesap kiracılar üstü olduğundan kiracı yok, ADR-031 K4). İkisi birlikte dolu olmaz.
/// </summary>
public sealed class LegalAcceptance
{
    public const int MaxVersionLength = 20;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid? UserId { get; private set; }
    public Guid? TenantId { get; private set; }
    public Guid? OwnerAccountId { get; private set; }
    public LegalDocumentType DocumentType { get; private set; }
    public string Version { get; private set; } = default!;
    public DateTime AcceptedAtUtc { get; private set; }

    private LegalAcceptance() { }

    /// <summary>Personel kullanıcısının kabulü (kiracı bağlamında).</summary>
    public static LegalAcceptance Record(
        Guid userId,
        Guid tenantId,
        LegalDocumentType documentType,
        string version,
        DateTime utcNow)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId geçersiz.", nameof(userId));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));

        var acceptance = Create(documentType, version, utcNow);
        acceptance.UserId = userId;
        acceptance.TenantId = tenantId;
        return acceptance;
    }

    /// <summary>Hasta sahibi portal hesabının kabulü (ADR-031 K4).</summary>
    public static LegalAcceptance RecordForOwnerAccount(
        Guid ownerAccountId,
        LegalDocumentType documentType,
        string version,
        DateTime utcNow)
    {
        if (ownerAccountId == Guid.Empty)
            throw new ArgumentException("OwnerAccountId geçersiz.", nameof(ownerAccountId));

        var acceptance = Create(documentType, version, utcNow);
        acceptance.OwnerAccountId = ownerAccountId;
        return acceptance;
    }

    private static LegalAcceptance Create(LegalDocumentType documentType, string version, DateTime utcNow)
    {
        if (!Enum.IsDefined(documentType))
            throw new ArgumentOutOfRangeException(nameof(documentType), "Belge türü geçersiz.");
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Sürüm boş olamaz.", nameof(version));

        var trimmed = version.Trim();
        if (trimmed.Length > MaxVersionLength)
            throw new ArgumentException($"Sürüm en fazla {MaxVersionLength} karakter olabilir.", nameof(version));

        return new LegalAcceptance
        {
            DocumentType = documentType,
            Version = trimmed,
            AcceptedAtUtc = utcNow.Kind == DateTimeKind.Utc ? utcNow : utcNow.ToUniversalTime(),
        };
    }
}
