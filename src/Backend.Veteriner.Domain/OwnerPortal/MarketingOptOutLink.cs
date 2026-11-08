using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.OwnerPortal;

/// <summary>
/// Ticari iletideki ret bağlantısı (ADR-046 K3). İleti başına bir kayıt; ham token yalnız e-postadadır, depoda SHA-256 özeti tutulur
/// (TIER-019 deseni). Tek kullanımlık değildir ve süresi dolmaz: ret her iletiden, her zaman tek adımda yapılabilir; tekrar kullanım
/// durumu değiştirmez. Ret, bağdaki onayı geri alır (<see cref="OwnerAccountClientLink.SetMarketingConsent"/>).
/// </summary>
public sealed class MarketingOptOutLink : ITenantOwned
{
    public const int TokenHashLength = 64;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid OwnerAccountClientLinkId { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Bağlantıyla ilk ret zamanı; sonraki kullanımlar değiştirmez.</summary>
    public DateTime? UsedAtUtc { get; private set; }

    private MarketingOptOutLink() { }

    public static MarketingOptOutLink Create(Guid tenantId, Guid clinicId, Guid ownerAccountClientLinkId, string tokenHash, DateTime utcNow)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));
        if (ownerAccountClientLinkId == Guid.Empty)
            throw new ArgumentException("Bağ geçersiz.", nameof(ownerAccountClientLinkId));
        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new ArgumentException("Token özeti boş olamaz.", nameof(tokenHash));

        return new MarketingOptOutLink
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            OwnerAccountClientLinkId = ownerAccountClientLinkId,
            TokenHash = tokenHash,
            CreatedAtUtc = utcNow,
        };
    }

    public void MarkUsed(DateTime utcNow) => UsedAtUtc ??= utcNow;
}
