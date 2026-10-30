using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.AppointmentRequests;

/// <summary>
/// Kliniğe özel bağlantıdan gelen kişinin e-posta doğrulama kodu (ADR-042 K1/K6): doğrulanmadan talep oluşmaz. Kod e-posta + klinik
/// için üretilir; düz kod saklanmaz, özet satır kimliğiyle tuzlanır (uygulama katmanı hesaplar). Uzunluk, süre, deneme hakkı ve
/// bekleme süresi portal kodlarıyla aynıdır (<see cref="OwnerPortalCode"/> sabitleri).
/// </summary>
public sealed class AppointmentRequestEmailVerification : ITenantOwned
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public string Email { get; private set; } = default!;
    public string CodeHash { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public int FailedAttempts { get; private set; }
    public DateTime? ConsumedAtUtc { get; private set; }
    public DateTime? InvalidatedAtUtc { get; private set; }

    private AppointmentRequestEmailVerification() { }

    /// <param name="id">Özet tuzu olarak kullanıldığı için özetten önce üretilir.</param>
    /// <param name="normalizedEmail">Küçük harfe indirilmiş e-posta (<see cref="AppointmentRequest.NormalizeEmail"/>).</param>
    public static AppointmentRequestEmailVerification Issue(
        Guid id,
        Guid tenantId,
        Guid clinicId,
        string normalizedEmail,
        string codeHash,
        DateTime utcNow)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || clinicId == Guid.Empty)
            throw new ArgumentException("Kimlik, kiracı ve klinik zorunludur.");
        if (string.IsNullOrWhiteSpace(normalizedEmail))
            throw new ArgumentException("E-posta boş olamaz.", nameof(normalizedEmail));
        if (string.IsNullOrWhiteSpace(codeHash))
            throw new ArgumentException("Kod özeti boş olamaz.", nameof(codeHash));

        return new AppointmentRequestEmailVerification
        {
            Id = id,
            TenantId = tenantId,
            ClinicId = clinicId,
            Email = normalizedEmail,
            CodeHash = codeHash,
            CreatedAtUtc = utcNow,
            ExpiresAtUtc = utcNow.Add(OwnerPortalCode.Lifetime),
        };
    }

    public bool IsActive(DateTime utcNow) =>
        ConsumedAtUtc is null && InvalidatedAtUtc is null && FailedAttempts < OwnerPortalCode.MaxFailedAttempts && utcNow < ExpiresAtUtc;

    public bool IsInResendCooldown(DateTime utcNow) => utcNow - CreatedAtUtc < OwnerPortalCode.ResendCooldown;

    public void Invalidate(DateTime utcNow) => InvalidatedAtUtc ??= utcNow;

    /// <summary>
    /// Doğrulama denemesi: eşleşmezse deneme sayılır, hak bitince kod geçersiz olur; eşleşirse kod tüketilir (tek kullanımlık).
    /// </summary>
    public bool TryConsume(bool codeMatches, DateTime utcNow)
    {
        if (!IsActive(utcNow))
            return false;

        if (!codeMatches)
        {
            FailedAttempts++;
            if (FailedAttempts >= OwnerPortalCode.MaxFailedAttempts)
                InvalidatedAtUtc = utcNow;
            return false;
        }

        ConsumedAtUtc = utcNow;
        return true;
    }
}
