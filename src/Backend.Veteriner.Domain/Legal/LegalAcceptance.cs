namespace Backend.Veteriner.Domain.Legal;

/// <summary>
/// Bir kullanıcının bir hukuki belge sürümünü kabul ettiği an (ADR-029). Değiştirilmez ve silinmez;
/// her kabul yeni satırdır. Veri azaltma gereği IP/cihaz bilgisi tutulmaz.
/// </summary>
public sealed class LegalAcceptance
{
    public const int MaxVersionLength = 20;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid UserId { get; private set; }
    public Guid TenantId { get; private set; }
    public LegalDocumentType DocumentType { get; private set; }
    public string Version { get; private set; } = default!;
    public DateTime AcceptedAtUtc { get; private set; }

    private LegalAcceptance() { }

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
        if (!Enum.IsDefined(documentType))
            throw new ArgumentOutOfRangeException(nameof(documentType), "Belge türü geçersiz.");
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Sürüm boş olamaz.", nameof(version));

        var trimmed = version.Trim();
        if (trimmed.Length > MaxVersionLength)
            throw new ArgumentException($"Sürüm en fazla {MaxVersionLength} karakter olabilir.", nameof(version));

        return new LegalAcceptance
        {
            UserId = userId,
            TenantId = tenantId,
            DocumentType = documentType,
            Version = trimmed,
            AcceptedAtUtc = utcNow.Kind == DateTimeKind.Utc ? utcNow : utcNow.ToUniversalTime(),
        };
    }
}
